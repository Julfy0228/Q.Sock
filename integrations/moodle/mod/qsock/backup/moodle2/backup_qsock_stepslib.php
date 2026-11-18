<?php
defined('MOODLE_INTERNAL') || die();

class backup_qsock_activity_structure_step extends backup_activity_structure_step {
    protected function define_structure(): backup_nested_element {
        $activity = new backup_nested_element('qsock', ['id'], ['name', 'intro', 'introformat', 'callid', 'timecreated', 'timemodified']);
        $activity->set_source_table('qsock', ['id' => backup::VAR_ACTIVITYID]);
        $activity->annotate_files('mod_qsock', 'intro', null);
        return $this->prepare_activity_structure($activity);
    }
}
