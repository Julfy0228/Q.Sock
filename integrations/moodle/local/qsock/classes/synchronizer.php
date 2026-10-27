<?php
namespace local_qsock;

defined('MOODLE_INTERNAL') || die();

final class synchronizer {
    public static function user(object $user): array {
        global $DB;
        $saved = $DB->get_record('local_qsock_users', ['userid' => $user->id]);
        $subject = $user->deleted && $saved ? $saved->subject : client::subject($user);
        if (!$saved) {
            $DB->insert_record('local_qsock_users', (object)['userid' => $user->id, 'subject' => $subject]);
        } else if ($saved->subject !== $subject && !$user->deleted) {
            $saved->subject = $subject;
            $DB->update_record('local_qsock_users', $saved);
        }

        $student = $DB->record_exists_sql(
            'SELECT 1 FROM {role_assignments} assignments JOIN {role} roles ON roles.id = assignments.roleid '
            . 'WHERE assignments.userid = ? AND roles.archetype = ?',
            [$user->id, 'student']
        );

        return [
            'subject' => $subject,
            'userName' => $user->username,
            'displayName' => fullname($user),
            'isAdmin' => is_siteadmin($user),
            'suspended' => !empty($user->suspended) || !empty($user->deleted),
            'isStudent' => $student,
        ];
    }

    public static function sync(?int $courseid = null): void {
        global $DB, $CFG;
        $payload = ['users' => [], 'courses' => [], 'fullSnapshot' => $courseid === null];
        $knownusers = [];
        $courses = $courseid === null
            ? $DB->get_records_select('course', 'id <> ?', [SITEID])
            : $DB->get_records('course', ['id' => $courseid]);

        foreach ($courses as $course) {
            $context = \context_course::instance($course->id);
            $members = [];
            foreach (get_enrolled_users($context, '', 0, 'u.*', 'u.id', 0, 0, true) as $user) {
                if (isguestuser($user) || $user->deleted) {
                    continue;
                }
                $knownusers[$user->id] = self::user($user);
                $groupnames = [];
                foreach (groups_get_all_groups($course->id, $user->id) as $group) {
                    $groupnames[] = $group->name;
                }
                $members[] = [
                    'subject' => $knownusers[$user->id]['subject'],
                    'role' => has_capability('moodle/course:manageactivities', $context, $user) ? 'teacher' : 'student',
                    'groups' => array_values($groupnames),
                ];
            }
            $payload['courses'][] = ['id' => (int)$course->id, 'name' => $course->fullname, 'members' => $members];
        }

        if ($courseid === null) {
            foreach ($DB->get_records_select('user', 'id <> ?', [$CFG->siteguest ?? 1]) as $user) {
                if (isguestuser($user) || ($user->deleted && !$DB->record_exists('local_qsock_users', ['userid' => $user->id]))) {
                    continue;
                }
                $knownusers[$user->id] = self::user($user);
            }
        }

        $payload['users'] = array_values($knownusers);
        client::request('sync', $payload);
    }
}
