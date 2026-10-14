from pathlib import Path
import subprocess,tempfile
source=(Path(__file__).resolve().parent / 'daisyos-native-development-session').read_text()
function=source[source.index('run_shell_supervisor() {'):source.index('\nstop_session_child()')]
for status, healthy in ((0, False), (23, False), (143, False), (137, False), (23, True)):
 with tempfile.TemporaryDirectory() as d:
  root=Path(d); shell=root/'app'; shell.write_text('#!/bin/bash\n' + ({143: 'kill -TERM $$', 137: 'kill -KILL $$'}.get(status, f'exit {status}')) + '\n'); shell.chmod(0o755)
  harness=f'''set -Eeuo pipefail
kwin_pid=$$
SECONDS=0
runs=0
wait() {{
 local result=0
 builtin wait "$@" || result=$?
 {'SECONDS=$((SECONDS+60))' if healthy else ':'}
 return "$result"
}}
native_shell_binary() {{ echo '{shell}'; }}
log_message() {{ :; }}
write_runtime_value() {{ :; }}
remove_runtime_value() {{ :; }}
consume_controlled_restart_request() {{ return 1; }}
sleep() {{
 runs=$((runs+1))
 echo "$1"
 if (( runs == 6 )); then exit 0; fi
}}
{function}
run_shell_supervisor
'''
  result=subprocess.run(['bash'],input=harness,text=True,capture_output=True,timeout=10)
  assert result.returncode==0,(status,result.stderr)
  assert result.stdout.splitlines()==(['1']*6 if healthy else ['1','2','4','8','15','15']),(status,result.stdout)
print('Supervisor recovery: normal exit, crash, TERM, KILL bounded backoff and healthy-uptime reset passed.')
