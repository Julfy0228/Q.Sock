<?php
defined('MOODLE_INTERNAL') || die();

function local_qsock_before_footer(): string {
    global $PAGE, $USER, $CFG;
    if (!isloggedin() || isguestuser() || !get_config('local_qsock', 'replacechat') || !get_config('local_qsock', 'serverurl')) {
        return '';
    }

    $PAGE->requires->js(new moodle_url('/local/qsock/drawer.js'));
    $configuration = json_encode([
        'endpoint' => $CFG->wwwroot . '/local/qsock/embed.php',
        'sesskey' => sesskey(),
        'launch' => $CFG->wwwroot . '/local/qsock/launch.php',
        'title' => get_string('pluginname', 'local_qsock'),
    ], JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT);

    return '<script type="application/json" id="qsock-drawer-config">' . $configuration . '</script>';
}
