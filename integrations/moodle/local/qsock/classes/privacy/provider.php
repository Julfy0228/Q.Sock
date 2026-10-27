<?php
namespace local_qsock\privacy;

defined('MOODLE_INTERNAL') || die();

final class provider implements
    \core_privacy\local\metadata\provider,
    \core_privacy\local\request\plugin\provider {

    public static function get_metadata(\core_privacy\local\metadata\collection $collection): \core_privacy\local\metadata\collection {
        $collection->add_database_table('local_qsock_users', [
            'userid' => 'privacy:metadata:local_qsock_users:userid',
            'subject' => 'privacy:metadata:local_qsock_users:subject',
        ], 'privacy:metadata:local_qsock_users');
        $collection->add_external_location_link('qsock', [
            'identity' => 'privacy:metadata:qsock:identity',
        ], 'privacy:metadata:qsock');
        return $collection;
    }

    public static function get_contexts_for_userid(int $userid): \core_privacy\local\request\contextlist {
        $contexts = new \core_privacy\local\request\contextlist();
        $contexts->add_from_sql('SELECT ctx.id FROM {context} ctx JOIN {local_qsock_users} mapping ON mapping.userid = ctx.instanceid WHERE ctx.contextlevel = ? AND mapping.userid = ?', [CONTEXT_USER, $userid]);
        return $contexts;
    }

    public static function export_user_data(\core_privacy\local\request\approved_contextlist $contextlist): void {
        global $DB;
        $userid = $contextlist->get_user()->id;
        $mapping = $DB->get_record('local_qsock_users', ['userid' => $userid]);
        if ($mapping) {
            \core_privacy\local\request\writer::with_context(\context_user::instance($userid))->export_data([], (object)['subject' => $mapping->subject]);
        }
    }

    public static function delete_data_for_all_users_in_context(\context $context): void {
        global $DB;
        if ($context->contextlevel === CONTEXT_USER) {
            $DB->delete_records('local_qsock_users', ['userid' => $context->instanceid]);
        }
    }

    public static function delete_data_for_user(\core_privacy\local\request\approved_contextlist $contextlist): void {
        foreach ($contextlist->get_contexts() as $context) {
            self::delete_data_for_all_users_in_context($context);
        }
    }
}
