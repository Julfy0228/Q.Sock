<?php
defined('MOODLE_INTERNAL') || die();

$events = [
    '\core\event\user_enrolment_created',
    '\core\event\user_enrolment_updated',
    '\core\event\user_enrolment_deleted',
    '\core\event\role_assigned',
    '\core\event\role_unassigned',
    '\core\event\group_member_added',
    '\core\event\group_member_removed',
    '\core\event\group_updated',
    '\core\event\group_deleted',
    '\core\event\user_updated',
    '\core\event\user_deleted',
    '\core\event\course_updated',
    '\core\event\course_deleted',
];

$observers = array_map(static function(string $event): array {
    return [
        'eventname' => $event,
        'callback' => '\local_qsock\observer::changed',
    ];
}, $events);
