import argparse
from datetime import datetime, timedelta, timezone
import http.server
import json
import os
from pathlib import Path
import subprocess
import sys
import threading
import time
import urllib.parse
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT / 'tests'))
import moodle_smoke

PASSWORD = 'test-only-Password49'


class MoodleFixture(http.server.BaseHTTPRequestHandler):
    def do_POST(self):
        raw = self.rfile.read(int(self.headers.get('Content-Length', 0)))
        fields = urllib.parse.parse_qs(raw.decode())
        if self.path == '/login/token.php':
            valid = fields.get('username') == ['TestAdmin'] and fields.get('password') == [PASSWORD]
            data = {'token': 'frontend-fixture-token'} if valid else {'error': 'invalidlogin'}
        elif self.path == '/webservice/rest/server.php':
            data = {'subject': 'frontend-teacher', 'userName': 'TestAdmin', 'displayName': 'Богдан Турков', 'isAdmin': True, 'isStudent': False, 'suspended': False}
        else:
            data = {'ok': True}
        body = json.dumps(data, ensure_ascii=False).encode()
        self.send_response(200)
        self.send_header('Content-Type', 'application/json')
        self.end_headers()
        self.wfile.write(body)

    def log_message(self, *arguments):
        pass


def seed(base):
    moodle_smoke.SERVER = base
    users = [
        {'subject': 'frontend-teacher', 'userName': 'TestAdmin', 'displayName': 'Богдан Турков', 'isAdmin': True},
        {'subject': 'frontend-professor', 'userName': 'Professor', 'displayName': 'Преподаватель'},
        {'subject': 'frontend-alex', 'userName': 'Alexey', 'displayName': 'Алексей', 'isStudent': True},
        {'subject': 'frontend-maria', 'userName': 'Maria', 'displayName': 'Мария', 'isStudent': True},
    ]
    members = [{'subject': user['subject'], 'role': 'teacher' if index < 2 else 'student', 'groups': [] if index < 2 else ['ИСТ-123']} for index, user in enumerate(users)]
    moodle_smoke.signed('sync', {'users': users, 'courses': [
        {'id': 101, 'name': 'Проектирование ИС', 'members': members},
        {'id': 102, 'name': 'Управление данными', 'members': members},
    ], 'fullSnapshot': True})
    moodle_smoke.signed('sync', {'users': users, 'courses': [{'id': 201, 'name': 'ЦДО · Консультации', 'members': members}], 'fullSnapshot': True}, 'cdo')
    sessions = {user['subject']: moodle_smoke.launch(user['subject'], 101) for user in users}
    tokens = {subject: session['auth']['accessToken'] for subject, session in sessions.items()}
    teacher = tokens['frontend-teacher']
    people = {subject: moodle_smoke.request('/api/me', token=token) for subject, token in tokens.items()}
    all_chats = moodle_smoke.request('/api/chats', token=teacher)
    room = sessions['frontend-teacher']['chatId']
    room_info = next(chat for chat in all_chats if chat['id'] == room)
    discussion = next(chat['id'] for chat in all_chats if chat['guildId'] == room_info['guildId'] and chat['type'] == 'channel')
    def message(chat, subject, value):
        return moodle_smoke.request(f'/api/chats/{chat}/messages', 'POST', {'text': value, 'clientOperationId': str(uuid.uuid4())}, tokens[subject])
    message(discussion, 'frontend-professor', 'Доброе утро! Материалы занятия доступны в Moodle. Сегодня разбираем архитектуру приложения.')
    message(discussion, 'frontend-alex', 'Можно будет задать вопрос по связям таблиц?')
    message(discussion, 'frontend-teacher', 'Да, подготовил схему. Обсудим её на занятии.')
    message(discussion, 'frontend-maria', 'Ссылки открываются, спасибо!')
    direct = moodle_smoke.request('/api/chats', 'POST', {'type': 'direct', 'userIds': [people['frontend-alex']['id']]}, teacher)['id']
    message(direct, 'frontend-alex', 'Спасибо, всё получилось!')
    maria = moodle_smoke.request('/api/chats', 'POST', {'type': 'direct', 'userIds': [people['frontend-maria']['id']]}, teacher)['id']
    message(maria, 'frontend-maria', 'Отправлю материалы после пары.')
    group = moodle_smoke.request('/api/chats', 'POST', {'type': 'group', 'name': 'ИСТ-123', 'userIds': [people['frontend-alex']['id'], people['frontend-maria']['id']]}, teacher)['id']
    message(group, 'frontend-alex', 'Отправил схему. Проверьте, пожалуйста, связи.')
    message(group, 'frontend-teacher', '**Архитектура приложения**\n\nКлиент → API → хранилище.\n\n`:smile:` и :ChatBubble: теперь остаются текстом.')
    call = moodle_smoke.request('/api/calls', 'POST', {'chatId': room, 'ringAll': False}, teacher)
    lesson = moodle_smoke.request('/api/modules/moodle/lessons', 'POST', {
        'chatId': room, 'title': 'Практика · Архитектура приложения',
        'startsAtUtc': (datetime.now(timezone.utc) + timedelta(days=1)).isoformat(),
        'durationMinutes': 90, 'reminder': True, 'contactEmail': 'teacher@example.test',
        'materials': ['https://example.test/architecture.pdf'],
    }, teacher)
    return {'discussion': discussion, 'room': room, 'group': group, 'call': call['id'], 'lesson': lesson['id']}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--dotnet', required=True)
    parser.add_argument('--port', type=int, default=5210)
    args = parser.parse_args()
    directory = ROOT / 'artifacts' / 'frontend-stand' / uuid.uuid4().hex[:12]
    directory.mkdir(parents=True)
    fixture = http.server.ThreadingHTTPServer(('127.0.0.1', 0), MoodleFixture)
    threading.Thread(target=fixture.serve_forever, daemon=True).start()
    environment = dict(os.environ)
    environment.update({
        'ConnectionStrings__ChatDatabase': f'Data Source={directory / "chat.db"}',
        'Logging__LogLevel__Default': 'Warning',
        'Bootstrap__AdminUser': '',
        'Moodle__Enabled': 'true',
        'ServerPlugins__Directory': str(ROOT / 'artifacts/plugins/server'),
        'ClientPlugins__Directory': str(ROOT / 'artifacts/plugins/client'),
        'Moodle__IdentityAuthority': 'department',
    })
    for index, name in enumerate(('department', 'cdo')):
        prefix = f'Moodle__Connections__{index}__'
        environment.update({prefix + 'Id': name, prefix + 'Provider': 'university',
                            prefix + 'BaseUrl': f'http://127.0.0.1:{fixture.server_port}',
                            prefix + 'Secret': moodle_smoke.SECRET})
    log = (directory / 'server.log').open('w', encoding='utf-8')
    base = f'http://127.0.0.1:{args.port}'
    process = subprocess.Popen([args.dotnet, str(ROOT / 'Q.Sock.Server/bin/Debug/net10.0/Q.Sock.Server.dll'), '--urls', base],
                               cwd=ROOT / 'Q.Sock.Server', env=environment, stdout=log, stderr=subprocess.STDOUT,
                               creationflags=subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0)
    try:
        for _ in range(100):
            if process.poll() is not None:
                raise RuntimeError(f'Test server stopped; see {directory / "server.log"}')
            try:
                with urllib.request.urlopen(base + '/health', timeout=1):
                    break
            except OSError:
                time.sleep(.2)
        else:
            raise TimeoutError('Test server startup timeout')
        data = seed(base)
        data.update({'url': base + '/avalonia/index.html', 'user': 'TestAdmin', 'directory': str(directory)})
        (ROOT / 'artifacts/frontend-stand/current.json').write_text(json.dumps(data, ensure_ascii=False, indent=2), encoding='utf-8')
        print('Frontend stand ready: ' + data['url'], flush=True)
        process.wait()
    finally:
        process.terminate()
        process.wait(timeout=10)
        fixture.shutdown()
        fixture.server_close()
        log.close()


if __name__ == '__main__':
    main()
