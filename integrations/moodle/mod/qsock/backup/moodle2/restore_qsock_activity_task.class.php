<?php
defined('MOODLE_INTERNAL') || die();
require_once(__DIR__ . '/restore_qsock_stepslib.php');

class restore_qsock_activity_task extends restore_activity_task {
    protected function define_my_settings(): void {}

    protected function define_my_steps(): void {
        $this->add_step(new restore_qsock_activity_structure_step('qsock_structure', 'qsock.xml'));
    }

    public static function define_decode_contents(): array {
        return [new restore_decode_content('qsock', ['intro'], 'qsock')];
    }

    public static function define_decode_rules(): array {
        return [];
    }

    public static function define_restore_log_rules(): array {
        return [];
    }

    public static function define_restore_log_rules_for_course(): array {
        return [];
    }
}
