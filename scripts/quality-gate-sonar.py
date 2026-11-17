#!/usr/bin/env python3
"""Confere o quality gate do SonarCloud nos pushes (fora de PR).

No PR o scanner já espera o quality gate (sonar.qualitygate.wait=true) e reprova o build.
No push o scanner só envia a análise e este script decide pelo status do gate:

- ERROR: falha o build.
- OK: segue.
- NONE (o SonarCloud ainda não calculou o período de código novo da branch) ou gate
  indisponível (plano gratuito não expõe branches que não são a principal): segue com
  aviso, porque o gate já foi exigido no PR que trouxe o código.

Uso: quality-gate-sonar.py  (na raiz do repositório, depois do `sonarscanner end`; lê
SONAR_TOKEN do ambiente e o report-task.txt que o scanner grava em .sonarqube/out/.sonar)
"""
import json
import os
import sys
import time
import urllib.error
import urllib.request

REPORT_PADRAO = ".sonarqube/out/.sonar/report-task.txt"


def ler_report(caminho):
    dados = {}
    with open(caminho, encoding="utf-8") as arquivo:
        for linha in arquivo:
            chave, sep, valor = linha.rstrip("\n").partition("=")
            if sep:
                dados[chave] = valor
    return dados


def consultar(url, token):
    requisicao = urllib.request.Request(url, headers={"Authorization": f"Bearer {token}"})
    with urllib.request.urlopen(requisicao, timeout=30) as resposta:
        return json.load(resposta)


class GateIndisponivel(Exception):
    pass


def consultar_ou_indisponivel(url, token):
    try:
        return consultar(url, token)
    except urllib.error.HTTPError as erro:
        corpo = erro.read().decode("utf-8", errors="replace")[:300]
        if 400 <= erro.code < 500 and erro.code != 401:
            raise GateIndisponivel(f"HTTP {erro.code}: {corpo}") from erro
        raise


def esperar_analise(report, token, prazo):
    while True:
        tarefa = consultar_ou_indisponivel(report["ceTaskUrl"], token)["task"]
        if tarefa["status"] == "SUCCESS":
            return tarefa["analysisId"]
        if tarefa["status"] in ("FAILED", "CANCELED"):
            raise RuntimeError(f"processamento da análise terminou em {tarefa['status']}")
        if time.monotonic() > prazo:
            raise RuntimeError("tempo esgotado esperando o processamento da análise")
        time.sleep(5)


def main():
    token = os.environ["SONAR_TOKEN"]
    report = ler_report(REPORT_PADRAO)
    servidor = report["serverUrl"].rstrip("/")
    prazo = time.monotonic() + int(os.environ.get("SONAR_GATE_TIMEOUT", "300"))
    painel = report.get("dashboardUrl", servidor)

    try:
        analise = esperar_analise(report, token, prazo)
        status = consultar_ou_indisponivel(
            f"{servidor}/api/qualitygates/project_status?analysisId={analise}", token
        )["projectStatus"]
    except GateIndisponivel as motivo:
        print(f"::warning::O SonarCloud não expôs o quality gate desta branch ({motivo}). "
              "O gate já foi exigido no PR.")
        return 0
    except RuntimeError as erro:
        print(f"::error::SonarCloud: {erro}. Detalhes em {painel}")
        return 1

    situacao = status.get("status")
    if situacao == "ERROR":
        reprovadas = [c for c in status.get("conditions", []) if c.get("status") == "ERROR"]
        for condicao in reprovadas:
            print(f"::error::Quality gate reprovado: {condicao.get('metricKey')} = "
                  f"{condicao.get('actualValue')} (limite {condicao.get('errorThreshold')})")
        print(f"Detalhes em {painel}")
        return 1
    if situacao == "OK":
        print(f"Quality gate aprovado. Detalhes em {painel}")
        return 0
    print(f"::warning::Quality gate sem status ({situacao}): o SonarCloud ainda não calculou "
          f"o código novo desta branch. O gate já foi exigido no PR. Detalhes em {painel}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
