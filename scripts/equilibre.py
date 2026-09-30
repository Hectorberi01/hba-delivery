#!/usr/bin/env python3
"""Verifie l'equilibre des {} () [] d'un fichier C# ou Dart.

POURQUOI UN AUTOMATE ET PAS TROIS EXPRESSIONS REGULIERES. La version
precedente retirait les commentaires AVANT les chaines : « http://media:8080 »
y perdait tout ce qui suivait les deux barres, parentheses fermantes comprises,
et l'outil signalait un desequilibre dans du code parfaitement equilibre. Un
instrument qui crie au loup se fait ignorer le jour ou il a raison.
"""
import sys

def balance(src):
    i, n = 0, len(src)
    d = {'{': 0, '(': 0, '[': 0}
    ligne = 1
    while i < n:
        c = src[i]
        if c == '\n':
            ligne += 1; i += 1; continue
        # commentaires
        if c == '/' and i + 1 < n:
            if src[i+1] == '/':
                while i < n and src[i] != '\n': i += 1
                continue
            if src[i+1] == '*':
                i += 2
                while i + 1 < n and not (src[i] == '*' and src[i+1] == '/'):
                    if src[i] == '\n': ligne += 1
                    i += 1
                i += 2; continue
        # chaine verbatim C#  @"..."  (les "" sont un guillemet echappe)
        if c == '@' and i + 1 < n and src[i+1] == '"':
            i += 2
            while i < n:
                if src[i] == '"':
                    if i + 1 < n and src[i+1] == '"': i += 2; continue
                    i += 1; break
                if src[i] == '\n': ligne += 1
                i += 1
            continue
        # chaine ordinaire, simple ou double quote (Dart accepte les deux)
        if c in '"\'':
            triple = src[i:i+3] in ('"""', "'''")
            fin = src[i:i+3] if triple else c
            i += 3 if triple else 1
            while i < n:
                if src[i] == '\\': i += 2; continue
                if src[i:i+len(fin)] == fin: i += len(fin); break
                if src[i] == '\n':
                    ligne += 1
                    if not triple: break   # chaine non terminee : on ne mange pas le reste
                i += 1
            continue
        if c in '{([': d[c] += 1
        elif c == '}': d['{'] -= 1
        elif c == ')': d['('] -= 1
        elif c == ']': d['['] -= 1
        i += 1
    return d

code = 0
for p in sys.argv[1:]:
    try:
        d = balance(open(p, encoding='utf-8').read())
    except Exception as e:
        print(f"??  {p}  ({e})"); code = 1; continue
    ok = all(v == 0 for v in d.values())
    if not ok: code = 1
    print(f"{'ok ' if ok else 'NON'} {d}  {p}")
sys.exit(code)
