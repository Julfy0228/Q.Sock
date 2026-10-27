<?php
require_once(__DIR__ . '/../../config.php');

require_login();
$courseid = optional_param('courseid', 0, PARAM_INT);
$callid = optional_param('callid', '', PARAM_ALPHANUMEXT);
if ($courseid) {
    $course = $DB->get_record('course', ['id' => $courseid], '*', MUST_EXIST);
    require_login($course);
    $context = context_course::instance($courseid);
    if (!is_enrolled($context, $USER, '', true) && !has_capability('moodle/course:view', $context)) {
        throw new required_capability_exception($context, 'moodle/course:view', 'nopermissions', '');
    }
    \local_qsock\synchronizer::sync($courseid);
} else {
    \local_qsock\client::request('sync', [
        'users' => [\local_qsock\synchronizer::user($USER)],
        'courses' => [],
        'fullSnapshot' => false,
    ]);
}

$url = \local_qsock\client::launch($USER, $courseid, $callid ?: null);
header('Referrer-Policy: no-referrer');
header('Cache-Control: no-store');
header('Location: ' . $url);
exit;
