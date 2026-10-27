<?php
require_once(__DIR__ . '/../../config.php');

$id = required_param('id', PARAM_INT);
$module = get_coursemodule_from_id('qsock', $id, 0, false, MUST_EXIST);
$course = $DB->get_record('course', ['id' => $module->course], '*', MUST_EXIST);
$activity = $DB->get_record('qsock', ['id' => $module->instance], '*', MUST_EXIST);
require_login($course, true, $module);
require_capability('mod/qsock:view', context_module::instance($module->id));

\local_qsock\synchronizer::sync($course->id);
$url = \local_qsock\client::launch($USER, (int)$course->id, $activity->callid ?: null);
header('Referrer-Policy: no-referrer');
header('Cache-Control: no-store');
header('Location: ' . $url);
exit;
