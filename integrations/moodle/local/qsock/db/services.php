<?php
defined('MOODLE_INTERNAL') || die();

$functions = [
    'local_qsock_identity' => [
        'classname' => '\local_qsock\external\identity',
        'methodname' => 'execute',
        'description' => 'Return the current university identity to Q.Sock.',
        'type' => 'read',
        'ajax' => false,
        'loginrequired' => true,
    ],
];

$services = [
    'Q.Sock' => [
        'functions' => ['local_qsock_identity'],
        'restrictedusers' => 0,
        'enabled' => 1,
        'shortname' => 'qsock',
        'downloadfiles' => 0,
        'uploadfiles' => 0,
    ],
];
