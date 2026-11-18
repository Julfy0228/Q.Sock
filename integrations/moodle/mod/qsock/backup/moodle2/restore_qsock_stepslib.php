<?php
defined('MOODLE_INTERNAL') || die();

class restore_qsock_activity_structure_step extends restore_activity_structure_step {
    protected function define_structure(): array {
        return [$this->prepare_activity_structure(new restore_path_element('qsock', '/activity/qsock'))];
    }

    protected function process_qsock($data): void {
        global $DB;
        $data = (object)$data;
        $data->course = $this->get_courseid();
        $data->callid = null;
        $newid = $DB->insert_record('qsock', $data);
        $this->apply_activity_instance($newid);
    }

    protected function after_execute(): void {
        $this->add_related_files('mod_qsock', 'intro', null);
    }
}
