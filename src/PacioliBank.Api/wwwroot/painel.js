'use strict';

// Painel de evidencia (ADR-0011). Usa apenas a API publica do ledger, como
// qualquer integrador: nenhum endpoint proprio, nenhuma dependencia externa.
(() => {
  const CONTA = {
    concorrencia: '33333333-3333-3333-3333-333333333333',
    replay: '33333333-3333-3333-3333-333333333333',
    tempo: '44444444-4444-4444-4444-444444444444',
    origem: '55555555-5555-5555-5555-555555555555',
  };

  const rota = (conta) => `/api/v1/accounts/${conta}`;
  const novaChave = () => (crypto.randomUUID
    ? crypto.randomUUID()
    : `painel-${Date.now()}-${Math.random().toString(16).slice(2)}`);
  const agora = () => new Date().toISOString();
  const $ = (seletor) => document.querySelector(seletor);
  const saida = (nome) => $(`[data-saida="${nome}"]`);

  // Exibicao apenas: o valor continua trafegando como string (EF secao 8.2).
  const brl = (texto) => Number(texto).toLocaleString('pt-BR', { minimumFractionDigits: 2, maximumFractionDigits: 2 });
  const data = (iso) => new Date(iso).toLocaleDateString('pt-BR', { timeZone: 'UTC' });
  const dataHora = (iso) => new Date(iso).toLocaleString('pt-BR', { timeZone: 'UTC' }) + ' UTC';

  class ErroDaApi extends Error {
    constructor(status, problema) {
      super(`${status} ${problema?.code ?? ''} ${problema?.detail ?? ''}`.trim());
      this.status = status;
      this.problema = problema;
    }
  }

  async function lerJson(resposta) {
    const texto = await resposta.text();
    try {
      return { texto, json: texto ? JSON.parse(texto) : null };
    } catch {
      return { texto, json: null };
    }
  }

  async function escrever(conta, tipo, valor, chave, quando) {
    const resposta = await fetch(`${rota(conta)}/${tipo}`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json', 'Idempotency-Key': chave },
      body: JSON.stringify({ amount: valor, currency: 'BRL', occurredAt: quando }),
    });
    const { texto, json } = await lerJson(resposta);
    return {
      status: resposta.status,
      repetida: resposta.headers.get('Idempotency-Replayed') === 'true',
      texto,
      json,
    };
  }

  async function obter(url) {
    const resposta = await fetch(url);
    const { json } = await lerJson(resposta);
    if (!resposta.ok) {
      throw new ErroDaApi(resposta.status, json);
    }
    return json;
  }

  const posicao = (conta, instante) =>
    obter(`${rota(conta)}/balance${instante ? `?asOf=${encodeURIComponent(instante)}` : ''}`);

  async function extrato(conta, aposSequencia = 0) {
    const linhas = [];
    let cursor = aposSequencia > 0 ? String(aposSequencia) : null;
    do {
      const pagina = await obter(`${rota(conta)}/entries?limit=200${cursor ? `&cursor=${cursor}` : ''}`);
      linhas.push(...pagina.entries);
      cursor = pagina.nextCursor ?? null;
    } while (cursor);
    return linhas;
  }

  function exigir(resultado, esperado, oQue) {
    if (resultado.status !== esperado) {
      throw new ErroDaApi(resultado.status, resultado.json);
    }
    return resultado;
  }

  function metricas(alvo, itens) {
    alvo.replaceChildren(...itens.map(([rotulo, valor, classe]) => {
      const caixa = document.createElement('div');
      caixa.className = `metrica ${classe ?? ''}`;
      const r = document.createElement('span');
      r.className = 'rotulo';
      r.textContent = rotulo;
      const v = document.createElement('span');
      v.className = 'valor';
      v.textContent = valor;
      caixa.append(r, v);
      return caixa;
    }));
  }

  function tabela(alvo, cabecalho, linhas) {
    const thead = document.createElement('thead');
    const tr = document.createElement('tr');
    for (const titulo of cabecalho) {
      const th = document.createElement('th');
      th.textContent = titulo;
      tr.append(th);
    }
    thead.append(tr);
    const tbody = document.createElement('tbody');
    for (const linha of linhas) {
      const row = document.createElement('tr');
      for (const celula of linha) {
        const td = document.createElement('td');
        const [texto, classe] = Array.isArray(celula) ? celula : [celula];
        td.textContent = texto;
        if (classe) td.className = classe;
        row.append(td);
      }
      tbody.append(row);
    }
    alvo.replaceChildren(thead, tbody);
  }

  function falhar(alvo, erro) {
    alvo.className = 'detalhe erro';
    alvo.textContent = `Falha: ${erro.message}`;
    console.error(erro);
  }

  async function comBotao(botao, trabalho) {
    botao.disabled = true;
    try {
      await trabalho();
    } finally {
      botao.disabled = false;
    }
  }

  // ------------------------------------------------- 1. Disparo concorrente

  async function disparoConcorrente() {
    const conta = CONTA.concorrencia;
    const detalhe = saida('concorrencia-detalhe');
    detalhe.className = 'detalhe';
    detalhe.textContent = 'Preparando a conta: zerando a posição e creditando 100,00.';

    // Saldo para exatamente 10 debitos de 10,00, pelos endpoints publicos.
    const inicial = await posicao(conta);
    if (Number(inicial.balance) > 0) {
      exigir(await escrever(conta, 'debits', inicial.balance, novaChave(), agora()), 201);
    }
    exigir(await escrever(conta, 'credits', '100.00', novaChave(), agora()), 201);
    const antes = await posicao(conta);

    const celulas = saida('concorrencia-celulas');
    celulas.replaceChildren(...Array.from({ length: 50 }, () => {
      const c = document.createElement('div');
      c.className = 'celula';
      return c;
    }));

    detalhe.textContent = 'Disparando 50 débitos de 10,00 de uma vez.';
    const inicio = performance.now();
    const resultados = await Promise.all(Array.from({ length: 50 }, (_, i) =>
      escrever(conta, 'debits', '10.00', novaChave(), agora()).then((r) => {
        celulas.children[i].className = `celula ${r.status === 201 ? 'aceito' : r.status === 422 ? 'recusado' : 'falha'}`;
        return r;
      })));
    const duracao = Math.round(performance.now() - inicio);

    const aceitos = resultados.filter((r) => r.status === 201).length;
    const porSaldo = resultados.filter((r) => r.status === 422 && r.json?.code === 'INSUFFICIENT_FUNDS').length;
    const outros = resultados.length - aceitos - porSaldo;

    const final = await posicao(conta);
    const novos = await extrato(conta, antes.computedAtSequence);
    const menor = novos.reduce((m, l) => Math.min(m, Number(l.balanceAfter)), Number(antes.balance));

    metricas(saida('concorrencia-metricas'), [
      ['Aceitos', String(aceitos), aceitos === 10 ? 'ok' : 'erro'],
      ['Recusados por saldo', String(porSaldo), porSaldo === 40 ? 'ok' : 'erro'],
      ['Outros', String(outros), outros === 0 ? '' : 'erro'],
      ['Posição final', brl(final.balance), Number(final.balance) === 0 ? 'ok' : 'erro'],
      ['Menor posição registrada', brl(String(menor)), menor >= 0 ? 'ok' : 'erro'],
    ]);
    detalhe.textContent = `Concluído em ${duracao} ms. Cada lançamento grava a posição após ele; a menor delas mostra que a conta nunca ficou negativa. `
      + 'O navegador limita as conexões simultâneas por servidor, então a disputa aqui é menor que a do teste F07 da suíte, que dispara os 50 em paralelo real contra o PostgreSQL.';
  }

  // ------------------------------------------------- 2. Linha do tempo

  const ROTEIRO = [
    { dias: -30, tipo: 'credits', valor: '500.00' },
    { dias: -21, tipo: 'debits', valor: '120.00' },
    { dias: -14, tipo: 'credits', valor: '80.00' },
    { dias: -7, tipo: 'debits', valor: '200.00' },
    { dias: -2, tipo: 'credits', valor: '45.50' },
  ];

  let linhaDoTempo = [];
  let limites = null;
  let espera = null;

  function instanteDoControle(valor) {
    return new Date(limites.inicio + ((limites.fim - limites.inicio) * valor) / 1000).toISOString();
  }

  async function prepararLinhaDoTempo() {
    const conta = CONTA.tempo;
    linhaDoTempo = await extrato(conta);

    if (linhaDoTempo.length === 0) {
      const hoje = new Date();
      hoje.setUTCHours(12, 0, 0, 0);
      for (const passo of ROTEIRO) {
        const fato = new Date(hoje.getTime() + passo.dias * 86_400_000).toISOString();
        exigir(await escrever(conta, passo.tipo, passo.valor, novaChave(), fato), 201);
      }
      linhaDoTempo = await extrato(conta);
    }

    const fatos = linhaDoTempo.map((l) => Date.parse(l.occurredAt));
    limites = { inicio: Math.min(...fatos) - 86_400_000, fim: Date.now() - 1000 };

    saida('tempo-controle').hidden = false;
    const controle = $('[data-entrada="tempo"]');
    controle.value = '1000';
    await consultarInstante(instanteDoControle(1000));
  }

  async function consultarInstante(instante) {
    saida('tempo-instante').textContent = dataHora(instante);
    const p = await posicao(CONTA.tempo, instante);

    metricas(saida('tempo-metricas'), [
      ['Posição no instante', brl(p.balance), 'ok'],
      ['Lançamentos somados', String(p.entriesReplayed)],
      ['Até a sequência', String(p.computedAtSequence)],
    ]);

    const lista = saida('tempo-lancamentos');
    lista.replaceChildren(...linhaDoTempo.map((l) => {
      const item = document.createElement('li');
      item.className = Date.parse(l.occurredAt) <= Date.parse(instante) ? 'dentro' : 'fora';
      const fato = document.createElement('span');
      fato.textContent = `fato ${data(l.occurredAt)}`;
      const valor = document.createElement('span');
      valor.className = 'valor';
      valor.textContent = `${l.direction === 'Credit' ? '+' : '−'}${brl(l.amount)}`;
      const registro = document.createElement('span');
      registro.textContent = `registrado ${dataHora(l.recordedAt)}`;
      item.append(fato, valor, registro);
      return item;
    }));
  }

  // ------------------------------------------------- 3. Reenvio idempotente

  let ultimoReenvio = null;

  async function sha256(texto) {
    if (!crypto.subtle) return '(indisponível)';
    const bytes = await crypto.subtle.digest('SHA-256', new TextEncoder().encode(texto));
    return Array.from(new Uint8Array(bytes), (b) => b.toString(16).padStart(2, '0')).join('').slice(0, 16);
  }

  async function linhaDeEnvio(rotulo, r) {
    return [
      rotulo,
      [String(r.status), r.status < 300 ? 'ok' : 'erro'],
      r.repetida ? 'true' : '',
      r.json?.entryId ? `${r.json.entryId.slice(0, 8)}…` : (r.json?.code ?? ''),
      await sha256(r.texto),
    ];
  }

  async function reenvio() {
    const conta = CONTA.replay;
    const chave = novaChave();
    const quando = agora();
    const antes = await posicao(conta);

    const primeiro = await escrever(conta, 'credits', '25.00', chave, quando);
    const segundo = await escrever(conta, 'credits', '25.00', chave, quando);
    const depois = await posicao(conta);

    ultimoReenvio = { conta, chave, quando, linhas: [
      await linhaDeEnvio('1º', primeiro),
      await linhaDeEnvio('2º, mesma chave', segundo),
    ] };
    tabela(saida('replay-tabela'), ['Envio', 'HTTP', 'Replayed', 'Lançamento', 'SHA-256 do corpo'], ultimoReenvio.linhas);

    const identico = primeiro.texto === segundo.texto;
    const detalhe = saida('replay-detalhe');
    detalhe.className = identico ? 'detalhe' : 'detalhe erro';
    detalhe.textContent = `Corpo idêntico: ${identico ? 'sim' : 'NÃO'}. Posição antes: ${brl(antes.balance)}; após o 1º envio: ${brl(primeiro.json?.balanceAfter ?? '0')}; após o 2º: ${brl(depois.balance)}. `
      + 'O reenvio não criou lançamento: a posição só mudou uma vez.';
    $('[data-acao="replay-conflito"]').disabled = false;
  }

  async function reenvioComConflito() {
    if (!ultimoReenvio) return;
    const { conta, chave, quando } = ultimoReenvio;
    const terceiro = await escrever(conta, 'credits', '99.00', chave, quando);
    ultimoReenvio.linhas.push(await linhaDeEnvio('3º, outro valor', terceiro));
    tabela(saida('replay-tabela'), ['Envio', 'HTTP', 'Replayed', 'Lançamento', 'SHA-256 do corpo'], ultimoReenvio.linhas);
  }

  // ------------------------------------------------- 4. Origem do calculo

  async function origemDoCalculo() {
    const conta = CONTA.origem;
    const linhas = [];
    const registrar = (momento, p) => linhas.push([
      momento,
      String(p.computedAtSequence),
      [p.computedFrom, p.computedFrom === 'snapshot' ? 'ok' : ''],
      String(p.entriesReplayed),
      brl(p.balance),
    ]);
    const mostrar = () => tabela(saida('origem-tabela'), ['Momento', 'Sequência', 'computedFrom', 'entriesReplayed', 'Posição'], linhas);

    const antes = await posicao(conta);
    registrar('Antes', antes);
    mostrar();

    const resto = antes.computedAtSequence % 100;
    const ate = resto === 0 && antes.computedAtSequence > 0 ? 100 : 100 - resto;
    for (let i = 1; i <= ate; i++) {
      exigir(await escrever(conta, 'credits', '1.00', novaChave(), agora()), 201);
      if (i % 10 === 0 || i === ate) {
        metricas(saida('origem-metricas'), [['Créditos registrados', `${i} de ${ate}`]]);
      }
    }
    const naAncora = await posicao(conta);
    registrar(`Após ${ate} créditos: sequência múltipla de 100`, naAncora);

    exigir(await escrever(conta, 'credits', '1.00', novaChave(), agora()), 201);
    registrar('Após mais 1 crédito', await posicao(conta));
    mostrar();

    metricas(saida('origem-metricas'), [
      ['Âncora do snapshot', `sequência ${naAncora.computedAtSequence}`, 'ok'],
      ['Somados na âncora', String(naAncora.entriesReplayed), naAncora.entriesReplayed === 0 ? 'ok' : 'erro'],
    ]);
  }

  // ------------------------------------------------- ligacoes

  const acoes = {
    concorrencia: [disparoConcorrente, 'concorrencia-detalhe'],
    'tempo-preparar': [prepararLinhaDoTempo, 'tempo-metricas'],
    replay: [reenvio, 'replay-detalhe'],
    'replay-conflito': [reenvioComConflito, 'replay-detalhe'],
    origem: [origemDoCalculo, 'origem-metricas'],
  };

  for (const [nome, [trabalho, alvoDeErro]] of Object.entries(acoes)) {
    const botao = $(`[data-acao="${nome}"]`);
    botao.addEventListener('click', () => comBotao(botao, async () => {
      try {
        await trabalho();
      } catch (erro) {
        falhar(saida(alvoDeErro), erro);
      }
    }));
  }

  $('[data-entrada="tempo"]').addEventListener('input', (evento) => {
    if (!limites) return;
    const instante = instanteDoControle(Number(evento.target.value));
    saida('tempo-instante').textContent = dataHora(instante);
    clearTimeout(espera);
    espera = setTimeout(() => consultarInstante(instante).catch((erro) => falhar(saida('tempo-metricas'), erro)), 120);
  });
})();
