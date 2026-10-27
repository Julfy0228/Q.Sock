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
        $form->addElement('text', 'callid', get_string('callid', 'qsock'));
        $form->setType('callid', PARAM_ALPHANUMEXT);
        $form->addHelpButton('callid', 'callid', 'qsock');
        $this->standard_coursemodule_elements();
        $this->add_action_buttons();
    }

    public function validation($data, $files): array {
        $errors = parent::validation($data, $files);
        if (!empty($data['callid']) && !preg_match('/^[0-9a-f-]{36}$/i', $data['callid'])) {
            $errors['callid'] = get_string('invalidcallid', 'qsock');
        }
        return $errors;
    }
}
