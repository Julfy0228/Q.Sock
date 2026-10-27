<?php
namespace local_qsock;

defined('MOODLE_INTERNAL') || die();

final class observer {
    public static function changed(\core\event\base $event): void {
        $task = new \local_qsock\task\sync_adhoc();
        $task->set_custom_data(['requested' => time()]);
        \core\task\manager::queue_adhoc_task($task, true);
    }
}
