<?php
defined('MOODLE_INTERNAL') || die();

if ($hassiteconfig) {
    $settings = new admin_settingpage('local_qsock', get_string('pluginname', 'local_qsock'));
    $settings->add(new admin_setting_configtext('local_qsock/serverurl', 'Q.Sock URL', '', '', PARAM_URL));
    $settings->add(new admin_setting_configtext('local_qsock/connectionid', 'Connection ID', '', 'department', PARAM_ALPHANUMEXT));
    $settings->add(new admin_setting_configpasswordunmask('local_qsock/secret', 'Shared secret', 'At least 32 random characters.', ''));
    $settings->add(new admin_setting_configselect('local_qsock/subjectfield', 'Common account identifier', '', 'username', [
        'username' => 'Username',
        'idnumber' => 'ID number',
    ]));
    $settings->add(new admin_setting_configcheckbox('local_qsock/replacechat', 'Replace message drawer', '', 1));
    $ADMIN->add('localplugins', $settings);
}
