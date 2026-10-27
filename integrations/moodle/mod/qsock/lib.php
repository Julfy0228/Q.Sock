<?php
defined('MOODLE_INTERNAL') || die();

function qsock_supports(string $feature) {
    switch ($feature) {
        case FEATURE_MOD_INTRO:
        case FEATURE_SHOW_DESCRIPTION:
        case FEATURE_BACKUP_MOODLE2:
            return true;
        case FEATURE_MOD_ARCHETYPE:
            return MOD_ARCHETYPE_RESOURCE;
        case FEATURE_GRADE_HAS_GRADE:
        case FEATURE_GROUPS:
            return false;
        default:
            return null;
    }
}

function qsock_add_instance(object $data, $form = null): int {
    global $DB;
    $data->timecreated = time();
    $data->timemodified = time();
    return $DB->insert_record('qsock', $data);
}

function qsock_update_instance(object $data, $form = null): bool {
    global $DB;
    $data->id = $data->instance;
    $data->timemodified = time();
    return $DB->update_record('qsock', $data);
}

function qsock_delete_instance(int $id): bool {
    global $DB;
    $DB->delete_records('qsock', ['id' => $id]);
    return true;
}
