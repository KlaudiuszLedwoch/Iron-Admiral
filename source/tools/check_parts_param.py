#!/usr/bin/env python3
"""Release check: every 'param' cell in parts.csv must have balanced brackets.
Usage: python check_parts_param.py <path to parts.csv>   (exit code 1 on errors)"""
import csv, sys
def main(path):
    rows = list(csv.reader(open(path, encoding='utf-8', newline='')))
    pi = rows[0].index('param')
    bad = []
    for n, r in enumerate(rows, 1):
        if len(r) <= pi or not r[0] or r[0].startswith('#'):
            continue
        depth = 0; ok = True
        for ch in r[pi]:
            depth += (ch == '(') - (ch == ')')
            if depth < 0 or depth > 1: ok = False
        if depth != 0: ok = False
        if not ok: bad.append((n, r[0], r[pi]))
    for n, name, p in bad:
        print(f'line {n}: {name}: {p}')
    print(f'{len(bad)} param cell(s) with unbalanced or nested brackets')
    return 1 if bad else 0
if __name__ == '__main__':
    sys.exit(main(sys.argv[1] if len(sys.argv) > 1 else 'parts.csv'))
