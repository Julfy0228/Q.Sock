<?php
namespace local_qsock\external;

defined('MOODLE_INTERNAL') || die();

use core_external\external_api;
use core_external\external_function_parameters;
use core_external\external_single_structure;
use core_external\external_value;

final class identity extends external_api {
    public static function execute_parameters(): external_function_parameters {
        return new external_function_parameters([]);
    }

    public static function execute(): array {
        global $USER;
        self::validate_context(\context_user::instance($USER->id));
        return \local_qsock\synchronizer::user($USER);
    }

    public static function execute_returns(): external_single_structure {
        return new external_single_structure([
            'subject' => new external_value(PARAM_RAW, 'Stable university account identifier'),
            'userName' => new external_value(PARAM_RAW, 'University login'),
            'displayName' => new external_value(PARAM_TEXT, 'Display name'),
            'isAdmin' => new external_value(PARAM_BOOL, 'Site administrator'),
            'suspended' => new external_value(PARAM_BOOL, 'Inactive account'),
            'isStudent' => new external_value(PARAM_BOOL, 'Student account'),
        ]);
    }
}
