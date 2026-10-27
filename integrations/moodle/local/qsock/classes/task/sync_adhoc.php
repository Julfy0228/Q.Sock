<?php
namespace local_qsock\task;

defined('MOODLE_INTERNAL') || die();

final class sync_adhoc extends \core\task\adhoc_task {
    public function execute(): void {
        if (get_config('local_qsock', 'serverurl')) {
            \local_qsock\synchronizer::sync();
        }
    }
}
