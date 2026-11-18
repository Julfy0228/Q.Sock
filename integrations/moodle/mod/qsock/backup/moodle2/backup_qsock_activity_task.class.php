<?php
defined('MOODLE_INTERNAL') || die();
require_once(__DIR__ . '/backup_qsock_stepslib.php');

class backup_qsock_activity_task extends backup_activity_task {
    protected function define_my_settings(): void {}

    protected function define_my_steps(): void {
        $this->add_step(new backup_qsock_activity_structure_step('qsock_structure', 'qsock.xml'));
    }

    public static function encode_content_links($content): string {
        return $content;
    }
}
