# CLAUDE.md — STO Center

> Documento de contexto para assistentes de IA. Leia este arquivo antes de qualquer tarefa de código.

---

## Visão Geral

**STO Center** (Santo Center) é um sistema web para gerenciamento completo de **assistências técnicas autônomas de TI**.

Centraliza o ciclo de vida de um atendimento técnico: cadastro de clientes e equipamentos, abertura de ordens de serviço, orçamentos, agendamento de atendimentos (com sincronização com o **Microsoft Outlook via Graph API**), contratos e faturamento.

---

## Stack Tecnológica

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10 |
| Backend | ASP.NET Core 10 (Minimal APIs) |
| Frontend | Next.js |
| ORM | Entity Framework Core (Npgsql) |
| Banco de Dados | PostgreSQL |
| Auth | JWT (Bearer Token) |
| Integração de Agenda | Microsoft Graph API (Outlook Calendar) |

---

## Arquitetura

Clean Architecture (Onion) com separação em 4 projetos sob `Host/src/`:

```
Host/
└── src/
    ├── stoCenter.Domain/          # Entidades, enums, regras de negócio puras. Sem dependências externas.
    ├── stoCenter.Application/     # Casos de uso, interfaces de repositório, DTOs, serviços de aplicação.
    │                              # Depende de: Domain
    ├── stoCenter.Infrastructure/  # EF Core, repositórios concretos, integrações externas (Graph API, etc.)
    │                              # Depende de: Application
    └── stoCenter.Server/          # Entry point ASP.NET Core, configuração de DI, Minimal API endpoints.
                                   # Depende de: Application + Infrastructure
Frontend/                          # Aplicação Next.js
```

**Regra de dependência:** `Domain ← Application ← Infrastructure ← Server`

---

## Modelo de Domínio

### Entidade Base

Todas as entidades herdam de `Entity`. Auditoria (`CriadoEm` / `AtualizadoEm`) é preenchida automaticamente via override de `SaveChanges` no EF Core — nunca manualmente.

```
Entity (abstract)
├── Id: Guid              →  uuid no PostgreSQL, gerado com Guid.CreateVersion7() (UUID v7 sequencial)
├── CriadoEm: DateTime    →  preenchido automaticamente no INSERT
└── AtualizadoEm: DateTime  →  atualizado automaticamente no UPDATE
```

> [!IMPORTANT]
> **IDs usam `Guid.CreateVersion7()`** (disponível no .NET 9+). UUIDs v7 são ordenados por tempo,
> eliminando a fragmentação de índice B-tree que UUIDs v4 aleatórios causam no PostgreSQL.
> Nunca use `Guid.NewGuid()` para gerar IDs de entidade.

---

### Cliente e Dados de Contato

```
Cliente
├── Id: Guid
├── Nome: string
├── Tipo: enum TipoCliente  →  PF | PJ
├── Documento: string       →  CPF (PF) ou CNPJ (PJ)
└── Ativo: bool             →  soft delete — nunca hard-delete um cliente com histórico

Telefone          →  N para 1 Cliente
├── Id: Guid
├── IdCliente: Guid
├── Telefone: string
└── Nome: string    →  ex: "Celular", "Comercial"

Email             →  N para 1 Cliente
├── Id: Guid
├── IdCliente: Guid
├── Email: string
└── Nome: string

Endereco          →  N para 1 Cliente
├── Id: Guid
├── IdCliente: Guid
├── Endereco: string
└── Nome: string    →  ex: "Sede", "Filial"
```

---

### Equipamento

```
Equipamento       →  N para 1 Cliente
├── Id: Guid
├── IdCliente: Guid
├── Tipo: enum TipoEquipamento  →  Desktop | Notebook | Servidor | Outro
├── Marca: string
├── Modelo: string
├── Ram: string?
├── Armazenamento: string?
├── Cpu: string?
├── Gpu: string?
├── SerialNumber: string?
├── Board: string?
├── Os: string?              →  Sistema Operacional
├── Hostname: string?
└── Observacoes: string?
```

---

### Tabela de Preços

Centraliza as tarifas padrão do negócio com suporte a vigência.
Ao agendar um `Atendimento`, os valores da tabela vigente são **copiados** (snapshot), garantindo que mudanças futuras de preço não alterem registros históricos.

```
TabelaPrecos
├── Id: Guid
├── Descricao: string                    →  ex: "Tabela 2026"
├── TarifaHoraPresencial: decimal        →  R$/hora para atendimentos presenciais
├── TarifaHoraRemoto: decimal            →  R$/hora para atendimentos remotos
├── ValorBancadaPorEquipamento: decimal  →  R$ por equipamento na bancada (valor padrão)
├── DataVigenciaInicio: DateOnly
├── DataVigenciaFim: DateOnly?           →  null = tabela ainda vigente
└── Ativo: bool
```

> [!IMPORTANT]
> Nunca use `TabelaPrecos` diretamente para calcular faturas históricas.
> O valor sempre vem do snapshot congelado no `Atendimento` e no `AtendimentoEquipamento`.

---

### Catálogo de Serviços

```
Servico           →  Catálogo base reutilizável
├── Id: Guid
├── Nome: string
├── Descricao: string
├── PrecoBancada: decimal?   →  preço fixo por equipamento (referência no catálogo)
├── PrecoHora: decimal?      →  cobrança por hora (referência no catálogo)
└── Ativo: bool              →  soft delete — serviço obsoleto não some do histórico de itens
```

---

### Ordem de Serviço (OS)

```
OrdemDeServico
├── Id: Guid
├── NumeroOs: int             →  número sequencial legível (SEQUENCE no PostgreSQL), único, nunca reutilizado
│                                Exibido como "OS-0047"
├── IdCliente: Guid
├── IdContrato: Guid?         →  nullable — vincula a um contrato ativo
├── DataAbertura: DateTime
├── DataPrevisao: DateTime?
├── DataFinalizacao: DateTime?
├── Descricao: string
├── ValorTotal: decimal       →  derivado dos atendimentos concluídos
├── StatusOs: enum            →  Aberta | EmAndamento | AguardandoCliente | Concluida | Cancelada
└── StatusFinanceiro: enum    →  Pendente | Pago
```

---

### Orçamento

```
Orcamento         →  N para 1 OS (múltiplas versões por OS)
├── Id: Guid
├── IdOrdemDeServico: Guid
├── Versao: int               →  inteiro incremental por OS — gerado pela OS ao criar um orçamento
│                                Invariante de domínio: não pode existir dois Orcamentos com mesma
│                                IdOrdemDeServico + Versao. Responsabilidade de OrdemDeServico.
├── DataCriacao: DateTime
├── DataValidade: DateOnly
├── Status: enum              →  Rascunho | Enviado | Aprovado | Recusado
├── ValorTotal: decimal       →  derivado dos itens
└── Observacoes: string?

ItemOrcamento     →  N para 1 Orçamento
├── Id: Guid
├── IdOrcamento: Guid
├── IdServico: Guid?          →  nullable — referência ao catálogo (opcional)
├── Descricao: string         →  preenchida manualmente ou herdada do catálogo
├── Quantidade: decimal
├── ValorUnitario: decimal
└── ValorSubtotal: decimal    →  SEMPRE calculado: Quantidade × ValorUnitario
```

---

### Atendimento *(integra com Microsoft Outlook Calendar)*

Representa a execução técnica de uma OS. Cada atendimento tem seu próprio número legível com prefixo de tipo.

```
Atendimento       →  N para 1 OS
├── Id: Guid
├── NumeroAtendimento: int    →  número sequencial global (SEQUENCE no PostgreSQL)
│                                Exibido com prefixo conforme Tipo:
│                                  Bancada   → "AT#B-0012"
│                                  Presencial → "AT#P-0047"
│                                  Remoto    → "AT#R-0003"
├── IdOrdemDeServico: Guid
├── IdTecnico: Guid
├── Tipo: enum TipoAtendimento  →  Bancada | Presencial | Remoto

│ ── Planejamento / Agenda ─────────────────────────────────────────
├── DataHoraAgendadaInicio: DateTime     →  horário local do servidor (OS do servidor é responsável pelo fuso)
├── DataHoraAgendadaFim: DateTime
├── FusoHorario: string                  →  string IANA — ex: "America/Sao_Paulo"
│                                           Usado exclusivamente para formatar o evento no Outlook Calendar.
│                                           Não altera a interpretação do DateTime armazenado.
├── TituloEvento: string                 →  ex: "AT#P-0047 - OS-0012 - Manutenção - Empresa X"
├── Localizacao: string?                 →  endereço físico ou "Bancada / Oficina"
├── LinkReuniaoRemota: string?           →  URL (Teams/AnyDesk/Meet) — obrigatório se Tipo = Remoto

│ ── Integração Microsoft Outlook (Graph API) ──────────────────────
├── OutlookEventId: string?              →  ID único retornado pela API (preenchido após sync)
├── OutlookICalUid: string?              →  UID iCalendar — garante idempotência na criação
├── OutlookChangeKey: string?            →  token de versão — detecta conflitos de edição paralela
├── StatusSincronizacao: enum            →  Pendente | Sincronizado | Erro

│ ── Execução Real (Apontamento Técnico) ───────────────────────────
├── DataHoraExecucaoInicio: DateTime?    →  quando o técnico deu "play"
├── DataHoraExecucaoFim: DateTime?       →  quando o técnico encerrou
├── DuracaoMinutosReal: int?             →  calculado (Fim − Início) ou sobrescrito manualmente

│ ── Precificação (snapshot congelado) ─────────────────────────────
├── IdTabelaPrecos: Guid?                →  referência à TabelaPrecos usada no snapshot (auditoria)
├── TarifaAplicada: decimal?             →  R$/hora — preenchido para Presencial e Remoto
│                                           null para Bancada (cobrança vem por equipamento)
├── ValorTotalAtendimento: decimal       →  calculado conforme tipo (ver regras abaixo)
├── Status: enum                         →  Agendado | EmAndamento | Concluido | Cancelado
└── Observacoes: string?
```

#### Regras de Cálculo por Tipo

| Tipo | Fórmula do `ValorTotalAtendimento` |
|---|---|
| **Presencial** | `(DuracaoMinutosReal / 60) × TarifaAplicada` + `Σ ServicoAtendimento.ValorSubtotal` |
| **Remoto** | `(DuracaoMinutosReal / 60) × TarifaAplicada` + `Σ ServicoAtendimento.ValorSubtotal` |
| **Bancada** | `Σ AtendimentoEquipamento.ValorBaseBancada` + `Σ ServicoAtendimento.ValorSubtotal` |

> [!NOTE]
> `TarifaAplicada` é **obrigatório** para Presencial e Remoto, e **nulo** para Bancada.
> O valor é copiado da `TabelaPrecos` vigente no momento do agendamento — nunca recalculado.

#### Detalhamento do Atendimento

```
AtendimentoEquipamento    →  N para 1 Atendimento
├── Id: Guid
├── IdAtendimento: Guid
├── IdEquipamento: Guid
├── ValorBaseBancada: decimal?   →  snapshot por equipamento
│                                   padrão = TabelaPrecos.ValorBancadaPorEquipamento
│                                   pode ser sobrescrito por negociação
├── DiagnosticoDefeito: string?
└── ObservacoesLaudo: string?

ServicoAtendimento        →  N para 1 Atendimento (serviços/peças executados)
├── Id: Guid
├── IdAtendimento: Guid
├── IdAtendimentoEquipamento: Guid?   →  nullable — associa o serviço a um equipamento específico
├── IdServico: Guid?                  →  nullable — referência ao catálogo
├── Descricao: string
├── Quantidade: decimal
├── ValorUnitario: decimal
└── ValorSubtotal: decimal            →  SEMPRE calculado: Quantidade × ValorUnitario
```

---

### Contrato

```
Contrato          →  N para 1 Cliente
├── Id: Guid
├── IdCliente: Guid
├── DataInicio: DateOnly
├── DataVencimento: DateOnly
├── DataFim: DateOnly?
├── Valor: decimal
├── Status: enum       →  Ativo | Cancelado | Finalizado
├── SrcDocumento: string?   →  caminho/URL do arquivo
└── Observacoes: string?
```

---

### Usuário / Técnico

```
Usuario
├── Id: Guid
├── Nome: string
├── Email: string
├── Telefone: string?
├── SenhaHash: string   →  bcrypt ou Argon2 — NUNCA texto puro
└── Ativo: bool         →  soft delete / inativação
```

---

## Enums

```
TipoCliente              →  PF | PJ
TipoEquipamento          →  Desktop | Notebook | Servidor | Outro
TipoAtendimento          →  Bancada | Presencial | Remoto
StatusOs                 →  Aberta | EmAndamento | AguardandoCliente | Concluida | Cancelada
StatusFinanceiro         →  Pendente | Pago
StatusOrcamento          →  Rascunho | Enviado | Aprovado | Recusado
StatusAtendimento        →  Agendado | EmAndamento | Concluido | Cancelado
StatusContrato           →  Ativo | Cancelado | Finalizado
StatusSincronizacao      →  Pendente | Sincronizado | Erro
```

---

## Diagrama de Relacionamentos

```
TabelaPrecos  ───────────────────────────────────────────────(snapshot)──┐
                                                                         │
Cliente ──┬── Telefone (N)                                               │
          ├── Email (N)                                                   │
          ├── Endereco (N)                                                │
          ├── Equipamento (N)                                             │
          ├── Contrato (N)                                                │
          └── OrdemDeServico (N)  [OS-NNNN]                              │
                  │                                                       │
                  ├── Orcamento (N)                                       │
                  │       └── ItemOrcamento (N) ── Servico? (catálogo)    │
                  │                                                       │
                  └── Atendimento (N)  [AT#B/P/R-NNNN] ─── IdTabelaPrecos ←─┘
                          ├── AtendimentoEquipamento (N) ── Equipamento
                          │       └── ValorBaseBancada (snapshot)
                          └── ServicoAtendimento (N) ─┬── Servico? (catálogo)
                                                      └── AtendimentoEquipamento? (vínculo)
```

---

## Numeração Legível

| Entidade | Formato | Exemplo | Geração |
|---|---|---|---|
| `OrdemDeServico` | `OS-NNNN` | `OS-0047` | SEQUENCE global no PostgreSQL |
| `Atendimento` (Bancada) | `AT#B-NNNN` | `AT#B-0012` | SEQUENCE global no PostgreSQL + prefixo por `Tipo` |
| `Atendimento` (Presencial) | `AT#P-NNNN` | `AT#P-0047` | idem |
| `Atendimento` (Remoto) | `AT#R-NNNN` | `AT#R-0003` | idem |

> [!NOTE]
> O campo `NumeroOs` / `NumeroAtendimento` armazena apenas o **inteiro** (ex: `47`).
> A formatação (`OS-0047`, `AT#B-0012`) é responsabilidade da camada de apresentação ou de um método do domínio.

---

## Fluxo Principal

```
1. Cadastrar TabelaPrecos vigente (tarifas do negócio)
2. Cadastrar Cliente → Equipamentos → (opcional) Contrato
3. Abrir Ordem de Serviço  →  NumeroOs gerado automaticamente
4. Criar Orçamento (versão 1) → Enviar → Cliente Aprova/Recusa
   └── Se recusado: OrdemDeServico cria nova versão (Versao++)
5. OS aprovada → Agendar Atendimento  →  NumeroAtendimento gerado + prefixo por Tipo
   ├── Snapshot: TarifaAplicada ← TabelaPrecos.TarifaHora{Tipo}  (Presencial/Remoto)
   ├── Snapshot: ValorBaseBancada ← TabelaPrecos.ValorBancadaPorEquipamento  (Bancada)
   ├── IdTabelaPrecos ← referência à tabela usada
   └── Sincroniza evento no Outlook Calendar via Microsoft Graph API
6. Técnico executa → Apontamento: DataHoraExecucaoInicio/Fim + ServicoAtendimento
7. Calcular ValorTotalAtendimento conforme tipo
8. OS Concluída → Faturamento (StatusFinanceiro: Pendente → Pago)
```

---

## Integração Microsoft Outlook Calendar

A sincronização de agenda usa a **Microsoft Graph API**:

| Campo no `Atendimento` | Finalidade |
|---|---|
| `OutlookEventId` | Identifica o evento na API para update/delete |
| `OutlookICalUid` | UID iCalendar — garante idempotência na criação |
| `OutlookChangeKey` | Token de versão — detecta conflitos de edição paralela |
| `StatusSincronizacao` | Rastreia o estado: `Pendente → Sincronizado \| Erro` |
| `FusoHorario` | String IANA passada ao criar/atualizar o evento no Outlook |
| `LinkReuniaoRemota` | URL da reunião (Teams/Meet/AnyDesk) — só para tipo `Remoto` |

**Ciclo de sincronização:**
- Agendamento criado/alterado → `StatusSincronizacao = Pendente` → worker envia para Graph API → `Sincronizado | Erro`
- Atendimento cancelado → evento removido do Outlook

> [!NOTE]
> A interface `IOutlookCalendarService` será definida na camada **Application** quando ela for implementada.

---

## Convenções e Decisões Técnicas

### Identificadores
- **IDs:** `Guid` em todas as entidades → mapeado para `uuid` no PostgreSQL
- **Geração:** sempre `Guid.CreateVersion7()` — UUID v7 sequencial, nunca `Guid.NewGuid()`
- **Números legíveis:** `NumeroOs` e `NumeroAtendimento` são `int` gerados por SEQUENCE no PostgreSQL

### Data e Hora
- **Tipo:** `DateTime` em todos os campos de data-hora
- **Timezone:** responsabilidade do sistema operacional do servidor — o servidor deve estar configurado com `America/Sao_Paulo` (ou o timezone do negócio)
- **`FusoHorario` no `Atendimento`:** string IANA usada exclusivamente para formatar eventos no Outlook Calendar — não altera a interpretação do `DateTime` armazenado

### Valores Monetários
- **Tipo C#:** `decimal`
- **Tipo PostgreSQL:** `numeric(10, 2)` — configurado globalmente no `DbContext` via convenção, não por anotação por propriedade
- **`ValorSubtotal`** (`ItemOrcamento`, `ServicoAtendimento`): sempre calculado (`Quantidade × ValorUnitario`) — nunca armazenar valor inconsistente

### Soft Delete
Entidades com `Ativo: bool` nunca devem ser hard-deletadas se possuem registros filhos:

| Entidade | Soft Delete |
|---|---|
| `Usuario` | ✅ `Ativo: bool` |
| `Cliente` | ✅ `Ativo: bool` |
| `Servico` | ✅ `Ativo: bool` |
| Demais | ❌ Hard delete controlado por FK |

EF Core: usar **global query filter** por entidade (`HasQueryFilter(e => e.Ativo)`) para que registros inativos sejam excluídos automaticamente das queries.

### Nullable Reference Types
- Habilitado em todos os projetos (`<Nullable>enable</Nullable>`)
- Campos opcionais são explicitamente `nullable` (`?`) — sem nulls implícitos

### Segurança
- `SenhaHash` — usar bcrypt ou Argon2 — **nunca armazenar senha em texto puro**
- JWT Bearer Token para autenticação da API

### Invariantes de Domínio
- `Orcamento.Versao` é incremental por OS — a `OrdemDeServico` é responsável por garantir a unicidade ao criar novos orçamentos (não deixar vazar para a camada de aplicação)
- `TarifaAplicada` no `Atendimento` é um **snapshot imutável** — nunca recalcular a partir da `TabelaPrecos`
- `ValorTotal` (`Orcamento`, `OrdemDeServico`) é **sempre derivado** dos filhos — nunca editado diretamente

---

## Configurações EF Core Relevantes

```csharp
// Configurações globais no OnModelCreating:

// 1. Decimal precision global para monetário
foreach (var property in modelBuilder.Model.GetEntityTypes()
    .SelectMany(e => e.GetProperties())
    .Where(p => p.ClrType == typeof(decimal) || p.ClrType == typeof(decimal?)))
{
    property.SetColumnType("numeric(10,2)");
}

// 2. Snake_case naming (padrão PostgreSQL)
modelBuilder.UseSnakeCaseNamingConvention(); // via EFCore.NamingConventions

// 3. Global query filters de soft delete
modelBuilder.Entity<Cliente>().HasQueryFilter(c => c.Ativo);
modelBuilder.Entity<Servico>().HasQueryFilter(s => s.Ativo);
modelBuilder.Entity<Usuario>().HasQueryFilter(u => u.Ativo);

// 4. Sequências para números legíveis
modelBuilder.HasSequence<int>("seq_numero_os").StartsAt(1).IncrementsBy(1);
modelBuilder.HasSequence<int>("seq_numero_atendimento").StartsAt(1).IncrementsBy(1);
modelBuilder.Entity<OrdemDeServico>()
    .Property(o => o.NumeroOs)
    .HasDefaultValueSql("nextval('seq_numero_os')");
modelBuilder.Entity<Atendimento>()
    .Property(a => a.NumeroAtendimento)
    .HasDefaultValueSql("nextval('seq_numero_atendimento')");
```

---

## Estrutura de Arquivos (Convenção Esperada)

```
Host/src/stoCenter.Domain/
├── Entities/
│   ├── Entity.cs                   # Classe base: Id (Guid v7), CriadoEm, AtualizadoEm
│   ├── Cliente.cs
│   ├── Telefone.cs
│   ├── Email.cs
│   ├── Endereco.cs
│   ├── Equipamento.cs
│   ├── TabelaPrecos.cs
│   ├── Servico.cs
│   ├── OrdemDeServico.cs           # Aggregate root: controla Versao do Orcamento
│   ├── Orcamento.cs
│   ├── ItemOrcamento.cs
│   ├── Atendimento.cs
│   ├── AtendimentoEquipamento.cs
│   ├── ServicoAtendimento.cs
│   ├── Contrato.cs
│   └── Usuario.cs
└── Enums/
    ├── TipoCliente.cs
    ├── TipoEquipamento.cs
    ├── TipoAtendimento.cs
    ├── StatusOs.cs
    ├── StatusFinanceiro.cs
    ├── StatusOrcamento.cs
    ├── StatusAtendimento.cs
    ├── StatusContrato.cs
    └── StatusSincronizacao.cs

Host/src/stoCenter.Application/
├── UseCases/
│   └── {Agregado}/
│       ├── Commands/
│       └── Queries/
├── Interfaces/        # IRepository<T>, IOutlookCalendarService (futuro)
└── DTOs/

Host/src/stoCenter.Infrastructure/
├── Persistence/
│   ├── AppDbContext.cs
│   ├── Configurations/  # IEntityTypeConfiguration<T> — uma por entidade
│   └── Repositories/
└── ExternalServices/    # OutlookGraphService (futuro)

Host/src/stoCenter.Server/
├── Endpoints/           # Minimal API endpoints por módulo
└── Program.cs

Frontend/                # Next.js
```
