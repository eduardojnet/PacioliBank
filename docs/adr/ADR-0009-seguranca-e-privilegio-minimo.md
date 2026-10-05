# ADR-0009: Garantir imutabilidade e sigilo por privilégio mínimo no banco e resposta opaca na API

- **Status:** Aceito
- **Data:** 2026-10-02
- **Decisor:** Eduardo J. G. do Carmo
- **Requisitos dirigentes:** RF-009, RNF-020 a RNF-027, CQ-05

## Contexto e problema

O enunciado identifica os dados como "financeiros sensíveis". No ordenamento brasileiro, isso significa duas camadas simultâneas: sigilo bancário, pela Lei Complementar 105/2001, e proteção de dados pessoais, pela Lei 13.709/2018.

Há duas perguntas de arquitetura a responder, e nenhuma delas é "qual biblioteca de autenticação usar".

**Primeira:** a imutabilidade do ledger ([ADR-0003](./ADR-0003-ledger-append-only.md)) é um requisito de auditoria e de integridade financeira. Se ela depender de o código nunca emitir `UPDATE` nem `DELETE`, depende de toda pessoa desenvolvedora presente e futura não errar, e de nenhum script administrativo ser executado contra a base. Isso não é um controle, é uma esperança.

**Segunda:** negar acesso a uma conta de terceiro parece trivial, mas a forma da negativa vaza informação. Responder `403` para conta existente e `404` para inexistente permite a qualquer pessoa autenticada enumerar quais contas existem. Em contexto de sigilo bancário, a existência da relação entre cliente e banco é, ela própria, informação protegida.

## Critérios de decisão

1. Controles que não dependem de disciplina humana para funcionar
2. Nenhum vazamento de existência de conta a terceiros
3. Dado pessoal ausente de log, traço e mensagem de erro
4. Minimização: o sistema não armazena dado pessoal que não precisa
5. Verificabilidade automatizada dos controles

## Opções consideradas

**Imutabilidade:** disciplina de código; gatilho de banco que rejeita alteração; ausência de privilégio.
**Autorização:** na borda da API; no domínio; em ambas.
**Negativa de acesso:** códigos distintos por situação; resposta uniforme.
**Dado do titular:** replicar CPF e nome; referenciar identificador opaco.

## Decisão

### 1. Imutabilidade por ausência de privilégio

Três papéis distintos no banco de dados:

> **Revisão de 2026-10-02 (lacuna L-05).** Os exemplos usavam o prefixo `app_*`. Substituído por `pacioli_*`, o nome efetivo em `db/init/001_roles_and_schema.sql` (desde o card 27, `db/migrations/0001_esquema_inicial.sql`), para evitar colisão em instância compartilhada ([convenções](../convencoes-de-nomenclatura.md) §5).

```sql
-- Migrações, exclusivamente. Nunca usado pela aplicação em execução.
CREATE ROLE pacioli_migrator;
GRANT ALL ON SCHEMA ledger TO pacioli_migrator;

-- Aplicação. Não possui UPDATE nem DELETE sobre o ledger.
CREATE ROLE pacioli_runtime;
GRANT SELECT, INSERT ON ledger.ledger_entries     TO pacioli_runtime;
GRANT SELECT, INSERT ON ledger.balance_snapshots  TO pacioli_runtime;
GRANT SELECT, INSERT ON ledger.idempotency_records TO pacioli_runtime;
GRANT SELECT, INSERT, UPDATE ON ledger.outbox_messages TO pacioli_runtime;
GRANT SELECT, UPDATE (last_sequence) ON ledger.accounts TO pacioli_runtime;
-- Card 32: fechamento diario, tabela derivada, corrigida na transacao do lancamento.
GRANT SELECT, INSERT, UPDATE ON ledger.daily_balances TO pacioli_runtime;

-- Consulta operacional e auditoria. Somente leitura.
CREATE ROLE pacioli_readonly;
GRANT SELECT ON ALL TABLES IN SCHEMA ledger TO pacioli_readonly;
```

`UPDATE` na tabela `accounts` é concedido **apenas sobre a coluna `last_sequence`**. Nem mesmo o status da conta pode ser alterado por este sistema, porque o ciclo de vida da conta pertence a outro contexto ([EF](../specs/EF-especificacao-funcional.md) §3.2). O privilégio reflete a fronteira de escopo, o que faz do banco de dados mais um guardião do risco R-07.

Com essa configuração, alterar um lançamento gravado é impossível para a aplicação, qualquer que seja o código. O controle sobrevive a erro humano, a refatoração mal feita e a script executado às pressas em produção.

**Por que não gatilho de banco:** um gatilho que rejeita `UPDATE` também é eficaz, mas pode ser desabilitado por quem tem privilégio de alteração de esquema, e sua existência é menos evidente em auditoria que uma listagem de permissões. A ausência de privilégio é mais simples e mais verificável.

### 2. Autorização em duas camadas

A borda valida a credencial e extrai o identificador do titular. O domínio valida que o titular da credencial é o titular da conta. Ambas as camadas, deliberadamente.

Razão: autorização apenas na borda falha quando um novo endpoint é criado e alguém esquece o atributo. Autorização no domínio falha fechado, porque a operação não executa sem o contexto de quem a solicitou.

### 3. Resposta opaca a terceiros

| Chamador | Conta inexistente | Conta de terceiro |
|---|---|---|
| Cliente final | `404 ACCOUNT_NOT_FOUND` | `404 ACCOUNT_NOT_FOUND` |
| Serviço interno com escopo amplo | `404 ACCOUNT_NOT_FOUND` | `403 FORBIDDEN` |

Para o cliente final, as duas situações são indistinguíveis, o que impede enumeração. Para serviço interno, que já opera com escopo sobre toda a base, a distinção é útil ao diagnóstico e não vaza nada que o chamador já não pudesse obter.

A tentativa negada é sempre registrada em auditoria com identificação do chamador, ainda que a resposta não revele nada.

> **Correção aos documentos de especificação.** A [EF](../specs/EF-especificacao-funcional.md) §8.6 e o [BDD](../specs/BDD-comportamento.md) F09 descrevem `403 FORBIDDEN` para acesso a conta de terceiro, enquanto o cenário exige que a existência não seja revelada. A regra acima resolve a contradição, e os dois documentos devem ser atualizados na próxima revisão. O registro desta divergência é intencional: especificação corrigida em silêncio perde a rastreabilidade de por que mudou. **Aplicada em 2026-10-02:** EF §8.6 e BDD F09 atualizados (lacuna L-05).

### 4. Minimização de dado pessoal

O sistema armazena `customer_id`, identificador opaco, e **não replica CPF, nome, endereço ou contato**. Nenhum desses dados é necessário para registrar lançamento ou calcular posição.

Consequência prática em caso de vazamento: o atacante obtém valores e vínculos a identificadores opacos, sem identificação direta dos titulares. A correlação exigiria comprometer também o sistema de cadastro.

O campo `metadata` é de preenchimento livre pelo originador. Como não há como garantir tecnicamente que ninguém envie dado pessoal nele, a proteção é contratual, reforçada por limite de tamanho e validação de formato, e declarada como risco residual.

### 5. Mascaramento em log

Serilog com enriquecedor de destruição aplicado a todo o grafo serializado: `customer_id` e `account_id` truncados aos últimos quatro caracteres; qualquer campo que corresponda a padrão de CPF ou de token substituído por marcador.

A verificação é automatizada: um teste exercita o caminho completo com dados sintéticos reconhecíveis e varre toda a saída de log em busca deles. Falha no teste se qualquer valor aparecer íntegro (RNF-021).

> **Estado em 2026-10-04 (card 33).** Implementado, com uma mudança de mecanismo: o mascaramento acontece no **formatador** que escreve a linha, e não num enriquecedor, porque o enriquecedor não alcança a mensagem renderizada nem o texto de exceção. Todo GUID sai truncado aos quatro últimos caracteres, e não só os de conta e cliente: num caminho de requisição não há como distinguir. CPF e token viram marcador. O teste descrito acima existe (`LogPipelineTests`, API e PostgreSQL reais). Decisão e alternativas no [ADR-0012](./ADR-0012-observabilidade.md).

### 6. Demais controles

| Controle | Implementação |
|---|---|
| Transporte | TLS 1.2 ou superior, obrigatório |
| Repouso | Criptografia no armazenamento e nos backups, pela plataforma |
| Credenciais | Fora do código e da imagem; varredura de segredos no pipeline |
| Dependências | Auditoria de pacotes no pipeline, bloqueando severidade alta |
| Limitação de taxa | Por chamador e por conta, com `429` e tempo de espera informado |
| Auditoria | Autor, instante, origem e correlação em toda escrita, na própria trilha imutável |

## Consequências

**Positivas**

- Imutabilidade do ledger garantida por mecanismo que independe de disciplina humana
- Enumeração de contas impossível para cliente final
- Superfície de exposição de dado pessoal reduzida por minimização, não por controle de acesso
- Controles verificáveis por teste automatizado, atendendo ao critério 5
- Privilégio de banco reforça a fronteira de escopo arquitetural

**Negativas**

- **Operação legítima de correção exige intervenção de papel privilegiado.** É a consequência pretendida, e precisa estar documentada no procedimento operacional, sob pena de uma emergência produzir a concessão apressada de privilégio excessivo
- Resposta uniforme dificulta o diagnóstico de integração: o parceiro não distingue identificador errado de falta de permissão. Mitigação: `correlationId` na resposta, permitindo investigação pelo suporte
- Autorização em duas camadas duplica a verificação, com custo pequeno de desempenho e de código
- Mascaramento em log dificulta investigação, exigindo consulta à base com papel de leitura para obter o valor íntegro
- A proteção do campo `metadata` é contratual, não técnica: é risco residual declarado

**Neutras**

- Três papéis de banco exigem gestão de três conjuntos de credenciais
- A decisão de não replicar dado do titular exige que consumidores de evento façam a correlação com o cadastro

## Análise das opções rejeitadas

**Imutabilidade por disciplina de código.** Rejeitada pelo critério 1. Controle que depende de ninguém errar não é controle, é expectativa.

**Gatilho de banco rejeitando alteração.** Eficaz, mas menos evidente em auditoria e desabilitável por quem altera esquema. *Permanece como reforço possível* em ambiente onde a gestão de privilégios não seja confiável.

**Autorização apenas na borda.** Rejeitada por falhar aberto: endpoint novo sem o atributo fica desprotegido, e o defeito é invisível até ser explorado.

**Códigos distintos para conta inexistente e conta de terceiro.** Rejeitada pelo critério 2, por permitir enumeração.

**Replicar CPF e nome do titular.** Rejeitada pelo critério 4. Aumentaria a superfície de exposição para resolver conveniência de consulta, que se resolve no sistema de cadastro.

## Validação

- [BDD](../specs/BDD-comportamento.md) F09, todos os cenários
- Teste de integração que tenta `UPDATE` e `DELETE` em `ledger_entries` com o papel `pacioli_runtime` e espera recusa por privilégio
- Teste de matriz de autorização cobrindo todas as combinações de papel e endpoint
- Varredura automatizada da saída de log em busca de dados sintéticos reconhecíveis
- Varredura de segredos e auditoria de dependências no pipeline

## Gatilho de revisão

1. Definição do modelo corporativo de identidade e de escopos (QA-007)
2. Requisito regulatório de retenção ou de anonimização (QA-004)
3. Necessidade de correlação direta com dado do titular dentro deste sistema, que reabriria a decisão de minimização
4. Incidente envolvendo dado pessoal no campo `metadata`, que exigiria controle técnico em vez de contratual
