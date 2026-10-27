<?php
require_once(__DIR__ . '/../../config.php');

$id = required_param('id', PARAM_INT);
$course = $DB->get_record('course', ['id' => $id], '*', MUST_EXIST);
require_login($course);
$PAGE->set_url('/mod/qsock/index.php', ['id' => $id]);
$PAGE->set_title(get_string('modulenameplural', 'qsock'));
$PAGE->set_heading($course->fullname);
echo $OUTPUT->header();
foreach (get_all_instances_in_course('qsock', $course) as $activity) {
    echo html_writer::div(html_writer::link(new moodle_url('/mod/qsock/view.php', ['id' => $activity->coursemodule]), format_string($activity->name)));
}
echo $OUTPUT->footer();
