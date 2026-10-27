<?php
require_once(__DIR__ . '/../../config.php');

require_login();
require_sesskey();
header('Content-Type: application/json');
header('Cache-Control: no-store');

\local_qsock\client::request('sync', [
    'users' => [\local_qsock\synchronizer::user($USER)],
    'courses' => [],
    'fullSnapshot' => false,
]);
echo json_encode(['url' => \local_qsock\client::launch($USER)]);
