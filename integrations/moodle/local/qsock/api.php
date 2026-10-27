<?php
define('NO_MOODLE_COOKIES', true);
define('AJAX_SCRIPT', true);
require_once(__DIR__ . '/../../config.php');
require_once($CFG->dirroot . '/calendar/lib.php');

header('Content-Type: application/json');
$calendarlock = null;

try {
    if ($_SERVER['REQUEST_METHOD'] !== 'POST') {
        http_response_code(405);
        exit;
    }

    $body = file_get_contents('php://input');
    $timestamp = (int)($_SERVER['HTTP_X_QSOCK_TIME'] ?? 0);
    $signature = $_SERVER['HTTP_X_QSOCK_SIGNATURE'] ?? '';
    $secret = get_config('local_qsock', 'secret');
    if (strlen($secret ?: '') < 32 || abs(time() - $timestamp) > 120) {
        throw new moodle_exception('invalidsignature', 'local_qsock');
    }
    if (!hash_equals(hash_hmac('sha256', $timestamp . "\n" . $body, $secret), $signature)) {
        throw new moodle_exception('invalidsignature', 'local_qsock');
    }

    $DB->delete_records_select('local_qsock_nonces', 'expires < ?', [time()]);
    if ($DB->record_exists('local_qsock_nonces', ['signature' => $signature])) {
        http_response_code(409);
        echo json_encode(['error' => 'Request already processed']);
        exit;
    }
    $DB->insert_record('local_qsock_nonces', (object)['signature' => $signature, 'expires' => time() + 180]);

    $data = json_decode($body, true, 512, JSON_THROW_ON_ERROR);
    if (($data['action'] ?? '') !== 'calendar') {
        throw new invalid_parameter_exception('Unsupported action');
    }
    $courseid = (int)$data['courseId'];
    $DB->get_record('course', ['id' => $courseid], '*', MUST_EXIST);
    $callid = clean_param($data['callId'], PARAM_ALPHANUMEXT);
    if (!preg_match('/^[0-9a-f-]{36}$/i', $callid)) {
        throw new invalid_parameter_exception('Invalid call ID');
    }

    $url = new moodle_url('/local/qsock/launch.php', ['courseid' => $courseid, 'callid' => $callid]);
    $record = (object)[
        'name' => clean_param($data['title'], PARAM_TEXT),
        'description' => html_writer::link($url, get_string('join', 'local_qsock')),
        'format' => FORMAT_HTML,
        'courseid' => $courseid,
        'groupid' => 0,
        'userid' => 0,
        'modulename' => '',
        'eventtype' => 'course',
        'timestart' => (int)$data['startsAt'],
        'timeduration' => min(28800, max(900, (int)$data['duration'])),
        'visible' => 1,
    ];

    $calendarlock = \core\lock\lock_config::get_lock_factory('local_qsock')->get_lock('calendar:' . $callid, 10);
    if (!$calendarlock) {
        throw new RuntimeException('Calendar update is already running');
    }
    $transaction = $DB->start_delegated_transaction();
    try {
        $mapping = $DB->get_record('local_qsock_events', ['callid' => $callid]);
        if ($mapping) {
            $event = calendar_event::load($mapping->eventid);
            $event->update($record, false);
        } else {
            $event = calendar_event::create($record, false);
            $DB->insert_record('local_qsock_events', (object)[
                'callid' => $callid,
                'eventid' => $event->id,
                'courseid' => $courseid,
            ]);
        }
        $transaction->allow_commit();
    } catch (Throwable $error) {
        $transaction->rollback($error);
    }
    echo json_encode(['eventId' => $event->id]);
} catch (Throwable $error) {
    http_response_code(400);
    echo json_encode(['error' => $error->getMessage()]);
} finally {
    if ($calendarlock) {
        $calendarlock->release();
    }
}
