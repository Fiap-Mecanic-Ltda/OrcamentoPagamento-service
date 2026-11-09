#!/bin/bash
# Executado automaticamente pelo LocalStack quando ele fica pronto (init/ready.d).
# Reproduz localmente a topologia que o Terraform cria na AWS:
#
#   ordem-servico-eventos (SNS)  -->  ordem-servico-itens-alterados (SQS)  -->  DLQ
#   orcamento-eventos     (SNS)  <--  publicado por este microsservico
set -euo pipefail

REGIAO="us-east-1"

echo "Criando topicos SNS..."
TOPICO_OS=$(awslocal sns create-topic --name ordem-servico-eventos --output text --query TopicArn)
awslocal sns create-topic --name orcamento-eventos >/dev/null

echo "Criando fila de mensagens mortas..."
awslocal sqs create-queue --queue-name ordem-servico-itens-alterados-dlq >/dev/null
DLQ_ARN=$(awslocal sqs get-queue-attributes \
  --queue-url "http://localhost:4566/000000000000/ordem-servico-itens-alterados-dlq" \
  --attribute-names QueueArn --output text --query 'Attributes.QueueArn')

echo "Criando fila principal com redrive para a DLQ..."
awslocal sqs create-queue \
  --queue-name ordem-servico-itens-alterados \
  --attributes "{\"RedrivePolicy\":\"{\\\"deadLetterTargetArn\\\":\\\"${DLQ_ARN}\\\",\\\"maxReceiveCount\\\":\\\"5\\\"}\",\"VisibilityTimeout\":\"60\"}" >/dev/null

FILA_ARN=$(awslocal sqs get-queue-attributes \
  --queue-url "http://localhost:4566/000000000000/ordem-servico-itens-alterados" \
  --attribute-names QueueArn --output text --query 'Attributes.QueueArn')

echo "Assinando a fila no topico da Ordem de Servico..."
# raw-message-delivery = true entrega o JSON do evento puro, sem o envelope do SNS.
# O consumidor aceita os dois formatos, mas o cru facilita inspecionar a fila na mao.
awslocal sns subscribe \
  --topic-arn "${TOPICO_OS}" \
  --protocol sqs \
  --notification-endpoint "${FILA_ARN}" \
  --attributes '{"RawMessageDelivery":"true","FilterPolicy":"{\"tipoEvento\":[\"ordem-servico.itens-alterados\"]}"}' >/dev/null

echo "Mensageria local pronta na regiao ${REGIAO}."
