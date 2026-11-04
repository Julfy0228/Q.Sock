<?php
namespace local_qsock;

defined('MOODLE_INTERNAL') || die();

final class client {
    public static function request(string $route, array $data): array {
        global $CFG;
        require_once($CFG->libdir . '/filelib.php');

        $config = get_config('local_qsock');
        if (empty($config->serverurl) || strlen($config->secret ?? '') < 32) {
            throw new \moodle_exception('notconfigured', 'local_qsock');
        }

        $data['requestId'] = bin2hex(random_bytes(16));
        $body = json_encode($data, JSON_UNESCAPED_UNICODE | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR);
        $timestamp = time();
        $signature = hash_hmac('sha256', $timestamp . "\n" . $body, $config->secret);
        $internal = get_config('local_qsock', 'internalurl');
        $parts = $internal ? parse_url($internal) : false;
        $local = $parts && ($parts['scheme'] ?? '') === 'http' && ($parts['host'] ?? '') === '127.0.0.1'
            && !empty($parts['port']) && empty($parts['user']) && empty($parts['pass']) && empty($parts['query'])
            && empty($parts['fragment']) && empty($parts['path']) && in_array($route, ['sync', 'launch'], true);
        $headers = ['Content-Type: application/json', 'X-QSock-Connection: ' . $config->connectionid,
            'X-QSock-Time: ' . $timestamp, 'X-QSock-Signature: ' . $signature];
        if ($local) {
            $handle = curl_init(rtrim($internal, '/') . '/api/modules/moodle/' . $route);
            curl_setopt_array($handle, [CURLOPT_POST => true, CURLOPT_POSTFIELDS => $body,
                CURLOPT_HTTPHEADER => $headers, CURLOPT_RETURNTRANSFER => true, CURLOPT_CONNECTTIMEOUT => 10,
                CURLOPT_TIMEOUT => 40, CURLOPT_FOLLOWLOCATION => false, CURLOPT_PROXY => '']);
            $response = curl_exec($handle);
            $status = curl_getinfo($handle, CURLINFO_HTTP_CODE);
            curl_close($handle);
        } else {
            $curl = new \curl();
            $curl->setHeader($headers);
            $apiurl = $internal ?: $config->serverurl;
            $response = $curl->post(rtrim($apiurl, '/') . '/api/modules/moodle/' . $route, $body, [
                'CURLOPT_CONNECTTIMEOUT' => 10, 'CURLOPT_TIMEOUT' => 40, 'CURLOPT_FOLLOWLOCATION' => 0,
            ]);
            $status = $curl->get_info()['http_code'] ?? 0;
        }
        if ($status < 200 || $status >= 300) {
            throw new \moodle_exception('servererror', 'local_qsock', '', $status);
        }

        return json_decode($response, true, 512, JSON_THROW_ON_ERROR);
    }

    public static function subject(object $user): string {
        $field = get_config('local_qsock', 'subjectfield') ?: 'username';
        $subject = (string)($user->{$field} ?? '');
        if ($subject === '' || strlen($subject) > 100) {
            throw new \moodle_exception('invalidsubject', 'local_qsock');
        }
        return $subject;
    }

    public static function launch(object $user, int $courseid = 0, ?string $callid = null): string {
        $response = self::request('launch', [
            'subject' => self::subject($user),
            'courseId' => $courseid,
            'callId' => $callid,
        ]);
        return rtrim(get_config('local_qsock', 'serverurl'), '/') . '/avalonia/index.html#launch=' . rawurlencode($response['code']);
    }
}
