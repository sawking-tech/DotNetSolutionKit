#!/usr/bin/env python3
"""Runs one job of a workflow a generated solution ships, on the machine it is called on.

    scripts/run-workflow.py <solution> <workflow.yml> <job> [name=value ...]

The template's CI uses it to run the shipped workflows as they are, without publishing the generated
solution anywhere: each `run:` step runs in order with the workflow's, the job's and the step's `env`,
in the solution's directory, under the shell GitHub uses (`bash --noprofile --norc -eo pipefail`).

What only GitHub has is handled the way GitHub does it, or refused:

- `${{ ... }}` is replaced by a value given as `name=value` on the command line, or by a step's output
  (`steps.<id>.outputs.<name>`). An expression with no value stops the run: a new expression in a
  shipped workflow is then seen here, not skipped.
- `uses:` steps of the actions below are passed by: the calling job checked out, installed the SDK and
  has nothing to cache or upload. Any other action stops the run.
- `if:` is `always()`, `success()`, `failure()` or a comparison of an expression with '', true or false
  (an input of type boolean given as true or false). After a failed step only `always()` and `failure()`
  steps run, and the run fails.
- `GITHUB_OUTPUT`, `GITHUB_ENV` and `GITHUB_STEP_SUMMARY` are files, read after each step.

The YAML is read through yq, which GitHub's Ubuntu runners have.
"""
import json
import os
import re
import subprocess
import sys
import tempfile

PASSED_BY = ("actions/checkout@", "actions/setup-dotnet@", "actions/cache@", "actions/upload-artifact@")
EXPRESSION = re.compile(r"\$\{\{\s*(.*?)\s*\}\}")


def load(path):
    out = subprocess.run(["yq", "-o=json", ".", path], check=True, capture_output=True, text=True).stdout
    return json.loads(out)


def scalar(value):
    # YAML's true and 12 come out of yq as JSON's true and 12; GitHub hands them on as "true" and "12".
    if isinstance(value, bool):
        return "true" if value else "false"
    return str(value)


def substitute(text, values):
    def one(match):
        name = match.group(1)
        if name not in values:
            raise SystemExit(f"run-workflow: no value for ${{{{ {name} }}}}; pass it as {name}=...")
        return values[name]

    return EXPRESSION.sub(one, scalar(text))


def condition(expression, values, failed):
    if expression is None:
        return not failed
    text = EXPRESSION.sub(lambda m: m.group(1), str(expression)).strip()
    if text == "always()":
        return True
    if text == "success()":
        return not failed
    if text == "failure()":
        return failed
    match = re.fullmatch(r"([\w.\-]+)\s*(==|!=)\s*(''|true|false)", text)
    if match:
        name, op, literal = match.groups()
        if name not in values:
            raise SystemExit(f"run-workflow: no value for {name} in if: {expression}")
        same = values[name] == ("" if literal == "''" else literal)
        return (not failed) and (same if op == "==" else not same)
    raise SystemExit(f"run-workflow: cannot evaluate if: {expression}")


def read_pairs(path):
    pairs = {}
    with open(path, encoding="utf-8") as f:
        for line in f.read().splitlines():
            if "<<" in line and "=" not in line.split("<<")[0]:
                raise SystemExit(f"run-workflow: multiline values in {path} are not supported")
            if "=" in line:
                key, value = line.split("=", 1)
                pairs[key] = value
    return pairs


def main():
    if len(sys.argv) < 4:
        raise SystemExit(__doc__)
    solution, workflow, job_id = sys.argv[1:4]
    values = dict(arg.split("=", 1) for arg in sys.argv[4:])
    document = load(os.path.join(solution, workflow))
    job = document["jobs"][job_id]
    env = {**os.environ, **{k: substitute(v, values) for k, v in (document.get("env") or {}).items()}}
    env.update({k: substitute(v, values) for k, v in (job.get("env") or {}).items()})
    env["GITHUB_WORKSPACE"] = os.path.abspath(solution)
    work = tempfile.mkdtemp(prefix="run-workflow-")
    env["GITHUB_STEP_SUMMARY"] = os.path.join(work, "summary.md")

    failed = False
    for number, step in enumerate(job["steps"], 1):
        name = step.get("name") or step.get("uses") or f"step {number}"
        if "uses" in step:
            if not step["uses"].startswith(PASSED_BY):
                raise SystemExit(f"run-workflow: {name}: the action {step['uses']} is not one the caller stands in for")
            print(f"--- {name}: passed by, the calling job did its part", flush=True)
            continue
        if not condition(step.get("if"), values, failed):
            print(f"--- {name}: skipped (if: {step.get('if', 'success()')})", flush=True)
            continue

        print(f"--- {name}", flush=True)
        output = os.path.join(work, f"output-{number}")
        github_env = os.path.join(work, f"env-{number}")
        open(output, "w").close()
        open(github_env, "w").close()
        step_env = {**env, "GITHUB_OUTPUT": output, "GITHUB_ENV": github_env}
        step_env.update({k: substitute(v, values) for k, v in (step.get("env") or {}).items()})
        script = os.path.join(work, f"step-{number}.sh")
        with open(script, "w", encoding="utf-8", newline="\n") as f:
            f.write(substitute(step["run"], values))
        cwd = os.path.join(solution, substitute(step.get("working-directory", "."), values))
        code = subprocess.run(["bash", "--noprofile", "--norc", "-eo", "pipefail", script], cwd=cwd, env=step_env).returncode
        if code != 0:
            print(f"::error::{name} failed with exit code {code}", flush=True)
            failed = True
            continue
        if "id" in step:
            for key, value in read_pairs(output).items():
                values[f"steps.{step['id']}.outputs.{key}"] = value
        env.update(read_pairs(github_env))

    sys.exit(1 if failed else 0)


if __name__ == "__main__":
    main()
