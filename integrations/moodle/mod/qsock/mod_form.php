<?php
defined('MOODLE_INTERNAL') || die();
require_once($CFG->dirroot . '/course/moodleform_mod.php');

class mod_qsock_mod_form extends moodleform_mod {
    public function definition(): void {
        $form = $this->_form;
        $form->addElement('header', 'general', get_string('general', 'form'));
        $form->addElement('text', 'name', get_string('name'));
        $form->setType('name', PARAM_TEXT);
        $form->addRule('name', null, 'required');
        $this->standard_intro_elements();
        $options = ['' => get_string('courseroom', 'qsock')];
        foreach ($this->course_lessons() as $lesson) {
            $options[$lesson->callid] = format_string($lesson->name) . ' — ' . userdate($lesson->timestart);
        }
        $saved = $this->current->callid ?? '';
        if ($saved !== '' && !isset($options[$saved])) {
            $options[$saved] = get_string('previouslesson', 'qsock');
        }
        $form->addElement('select', 'callid', get_string('callid', 'qsock'), $options);
        $form->setType('callid', PARAM_ALPHANUMEXT);
        $form->addHelpButton('callid', 'callid', 'qsock');
        $this->standard_coursemodule_elements();
        $this->add_action_buttons();
    }

    public function validation($data, $files): array {
        $errors = parent::validation($data, $files);
        $selected = $data['callid'] ?? '';
        if ($selected !== '' && $selected !== ($this->current->callid ?? '') && !isset($this->course_lessons()[$selected])) {
            $errors['callid'] = get_string('invalidcallid', 'qsock');
        }
        return $errors;
    }

    private function course_lessons(): array {
        global $DB;
        $sql = 'SELECT m.callid, e.name, e.timestart
                  FROM {local_qsock_events} m
                  JOIN {event} e ON e.id = m.eventid AND e.courseid = m.courseid
                 WHERE m.courseid = :courseid
              ORDER BY e.timestart DESC, e.id DESC';
        return $DB->get_records_sql($sql, ['courseid' => $this->get_course()->id]);
    }
}
