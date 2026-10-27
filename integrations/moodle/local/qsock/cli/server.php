<?php
const CLI_SCRIPT = true;
require(__DIR__ . '/../../../config.php');
require_once($CFG->libdir . '/clilib.php');

[$options, $unrecognized] = cli_get_params([
    'help' => false,
    'public-url' => '',
    'listen' => 'http://127.0.0.1:5210',
    'status' => false,
    'stop' => false,
], ['h' => 'help']);
if ($unrecognized) {
    cli_error('Unknown options: ' . implode(', ', $unrecognized));
}
if ($options['help']) {
    echo "Q.Sock embedded server\n"
        . "php local/qsock/cli/server.php --public-url=https://messenger.example.edu\n"
        . "Options: --listen=http://127.0.0.1:5210 --status --stop\n"
        . "Run under a service manager. Data and secrets stay in Moodle dataroot.\n";
    exit(0);
}
$directory = $CFG->dataroot . '/local_qsock/server';
if (!is_dir($directory) && !mkdir($directory, 0700, true)) {
    cli_error('Cannot create embedded server data directory.');
}
$lock = fopen($directory . '/process.lock', 'c');
if (!$lock) {
    cli_error('Cannot open server lock.');
}
$acquired = flock($lock, LOCK_EX | LOCK_NB);
if ($options['status']) {
    echo $acquired ? "Stopped\n" : "Running\n";
    fclose($lock);
    exit($acquired ? 1 : 0);
}
if ($options['stop']) {
    if (!$acquired) {
        touch($directory . '/stop');
        echo "Stop requested\n";
    } else {
        echo "Already stopped\n";
    }
    fclose($lock);
    exit(0);
}
if (!$acquired) {
    cli_error('Embedded server is already running.');
}
$bundle = realpath(__DIR__ . '/../server');
$binary = $bundle ? $bundle . '/Q.Sock.Server' . (PHP_OS_FAMILY === 'Windows' ? '.exe' : '') : '';
if (!$binary || !is_file($binary)) {
    cli_error('This plugin package has no embedded server for this platform. Install the matching bundle.');
}
if (PHP_OS_FAMILY !== 'Windows' && !is_executable($binary) && !chmod($binary, 0700)) {
    cli_error('Cannot make bundled server executable.');
}
$publicurl = rtrim($options['public-url'] ?: get_config('local_qsock', 'serverurl'), '/');
$parts = parse_url($publicurl);
$local = in_array($parts['host'] ?? '', ['127.0.0.1', 'localhost', '::1'], true);
if (!$parts || empty($parts['host']) || !empty($parts['user']) || !empty($parts['pass'])
        || !empty($parts['query']) || !empty($parts['fragment']) || !empty($parts['path'])
        || (($parts['scheme'] ?? '') !== 'https' && !(($parts['scheme'] ?? '') === 'http' && $local))) {
    cli_error('Set a root HTTPS public URL, or HTTP localhost for a local stand.');
}
$listen = parse_url($options['listen']);
if (!$listen || ($listen['scheme'] ?? '') !== 'http' || !in_array($listen['host'] ?? '', ['127.0.0.1', 'localhost', '::1'], true)) {
    cli_error('The embedded server must listen on loopback behind the HTTPS proxy.');
}
$configuration = $directory . '/appsettings.instance.json';
if (!is_file($configuration)) {
    $connectionid = get_config('local_qsock', 'connectionid') ?: 'department';
    $secret = get_config('local_qsock', 'secret');
    if (strlen($secret ?: '') < 32) {
        $secret = bin2hex(random_bytes(32));
        set_config('secret', $secret, 'local_qsock');
    }
    set_config('connectionid', $connectionid, 'local_qsock');
    $settings = [
        'ConnectionStrings' => ['ChatDatabase' => 'Data Source=' . $directory . '/chat.db'],
        'State' => ['Directory' => $directory . '/state'],
        'Storage' => ['Directory' => $directory . '/storage'],
        'ServerPlugins' => ['Directory' => $bundle . '/server-plugins'],
        'ClientPlugins' => ['Directory' => $bundle . '/client-modules'],
        'Authentication' => ['Provider' => 'moodle'],
        'Moodle' => [
            'Enabled' => true,
            'IdentityAuthority' => $connectionid,
            'Connections' => [[
                'Id' => $connectionid,
                'Provider' => 'university',
                'BaseUrl' => $CFG->wwwroot,
                'Secret' => $secret,
            ]],
        ],
    ];
    $file = fopen($configuration, 'x');
    if (!$file) {
        cli_error('Cannot create instance configuration.');
    }
    chmod($configuration, 0600);
    fwrite($file, json_encode($settings, JSON_PRETTY_PRINT | JSON_UNESCAPED_SLASHES | JSON_THROW_ON_ERROR));
    fclose($file);
}
set_config('serverurl', $publicurl, 'local_qsock');
set_config('internalurl', $options['listen'], 'local_qsock');
$stop = $directory . '/stop';
if (is_file($stop)) {
    unlink($stop);
}
$descriptors = [
    0 => ['pipe', 'r'],
    1 => ['file', $directory . '/server.log', 'a'],
    2 => ['file', $directory . '/server.log', 'a'],
];
$process = proc_open([$binary, '--contentRoot', $bundle, '--InstanceConfiguration', $configuration,
    '--urls', $options['listen']], $descriptors, $pipes, $directory, null, ['bypass_shell' => true]);
if (!is_resource($process)) {
    cli_error('Cannot start embedded server. Check proc_open permissions.');
}
fclose($pipes[0]);
echo "Embedded server started. Log: " . $directory . "/server.log\n";
try {
    while (true) {
        $state = proc_get_status($process);
        if (!$state['running']) {
            $exitcode = $state['exitcode'];
            break;
        }
        if (is_file($stop)) {
            proc_terminate($process);
            $exitcode = 0;
            break;
        }
        sleep(1);
    }
} finally {
    proc_close($process);
    if (is_file($stop)) {
        unlink($stop);
    }
    flock($lock, LOCK_UN);
    fclose($lock);
}
exit($exitcode < 0 ? 1 : $exitcode);
