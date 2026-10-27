<?php
namespace local_qsock\task;

defined('MOODLE_INTERNAL') || die();

final class sync_task extends \core\task\scheduled_task {
    public function get_name(): string {
        return get_string('sync', 'local_qsock');
    }

    public function execute(): void {
        if (get_config('local_qsock', 'serverurl')) {
            \local_qsock\synchronizer::sync();
        }
    }
}
