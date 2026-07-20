# PetAdoption API

Backend do site de adoção de pets. Projeto de estudo (Clean Architecture, DDD, mensageria assíncrona), mas deve ficar público e funcional.

## Stack e infraestrutura

- **Backend:** .NET 10 / C#, Clean Architecture, monólito modular
- **Banco:** PostgreSQL — Neon (serverless) em produção, Postgres via Docker Compose em dev
- **Mídia (fotos/vídeos de pets):** Cloudflare R2 (API compatível com S3, sem custo de egress)
- **Mensageria:** RabbitMQ via Docker Compose (dev/prod), biblioteca Wolverine (Outbox/Inbox nativo)
- **Hospedagem da API:** Render (free tier)
- **CI/CD:** GitHub Actions

## Comandos

- Build: `dotnet build`
- Testes:
  - `dotnet test --filter Category=Unit`
  - `dotnet test --filter Category=Integration` (Testcontainers + Postgres real)
  - `dotnet test --filter Category=Architecture` (NetArchTest — valida as regras de dependência abaixo)
- Formatação: `dotnet csharpier format .` antes de cada commit

## Autenticação

- Base: **ASP.NET Core Identity** (`ApplicationUser : IdentityUser<Guid>`) — cuida de hashing de senha, lockout, confirmação de e-mail
- Por cima: **JWT + refresh token construído do zero** — claims, expiração, rotação/revogação de refresh token

## Estrutura de solução

```
PetAdoption.sln
/src
  /BuildingBlocks
    BuildingBlocks.Domain/          -> Entity, AggregateRoot, ValueObject, IDomainEvent
    BuildingBlocks.Messaging/       -> infra do Outbox/Inbox, extensoes do Wolverine
  /Modules
    /Identity
      Identity.Domain/              -> Profile, ContactInfo
      Identity.Application/
      Identity.Infrastructure/      -> EF Core (schema "identity"), ApplicationUser, endpoints
    /PetPublishing
      PetPublishing.Domain/         -> Listing, Location, MediaItem
      PetPublishing.Application/
      PetPublishing.Infrastructure/ -> EF Core (schema "petpublishing"), endpoints, consumer de InterestCreated
      PetPublishing.Contracts/      -> ListingMarkedAsAdoptedIntegrationEvent
    /InterestMatching
      InterestMatching.Domain/      -> Interest
      InterestMatching.Application/
      InterestMatching.Infrastructure/ -> EF Core (schema "interestmatching"), consumer de ListingMarkedAsAdopted
      InterestMatching.Contracts/   -> InterestCreatedIntegrationEvent
  /Host
    PetAdoption.Api/                -> Program.cs (composition root)
/tests
  /UnitTests
  /IntegrationTests   -> Testcontainers com Postgres real + Wolverine em modo de teste
  /ArchitectureTests  -> PetAdoption.ArchitectureTests (NetArchTest)
```

`Identity` ainda não tem `.Contracts` — não publica nenhum evento de integração hoje. Só criar quando surgir necessidade real.

## Domínio por módulo

**Identity & Access** (subdomínio genérico)

- `ApplicationUser` — login/credenciais, genérico
- `Profile` (entidade): `UserId`, `Name`, `PhotoUrl`, `Description`, `ContactInfo` (value object: telefone/endereço + flag `Public | Private`), `CreatedAt`
- Invariante: `Profile` só existe se existir um `ApplicationUser` correspondente
- **Decisão deliberada:** não existe `UserType` (donor/adopter) como atributo de cadastro. Qualquer usuário pode publicar ou se interessar a qualquer momento — o papel emerge do comportamento, não é dado de cadastro

**Pet Publishing** (subdomínio de suporte)

- `Listing` (aggregate root): `DonorUserId`, dados do pet embutidos (nome, espécie, raça, idade, porte, descrição — sem entidade `Pet` própria por enquanto), `Location` (value object), `Media: List<MediaItem>` (value objects: url, tipo, ordem), `Status: Available | Adopted | Removed`, `CreatedAt`, `UpdatedAt`
- Ação: `MarkAsAdopted()`
- Publica: `ListingMarkedAsAdopted`. Consome: `InterestCreated` -> incrementa contador de interesses

**Interest & Matching** (subdomínio core — onde vale mais cuidado de modelagem)

- `Interest` (aggregate root): `ListingId`, `InterestedUserId`, `Message` (opcional), `CreatedAt`, `Status: Active | Cancelled | Closed`
- Invariantes: usuário não pode se interessar pela própria publicação; um `Interest` por par usuário+listing
- **Regra de negócio explícita:** um `Interest` ativo concede ao doador acesso ao perfil e contato do interessado, mesmo que o contato seja privado por padrão — precisa de consentimento claro na UI (relevante para LGPD)
- MVP sem aceite/recusa formal: doador acessa a lista de interessados manualmente, decide e contata por fora do sistema. Sem chat interno
- Publica: `InterestCreated`. Consome: `ListingMarkedAsAdopted` -> encerra os interesses ativos daquela listing

## Regras de dependência entre camadas (não negociável)

- `Domain` não referencia nada além das classes-base de `BuildingBlocks.Domain`
- `Application` referencia só o `Domain` do próprio módulo
- `Infrastructure` referencia `Application` + `Domain` do próprio módulo
- Um módulo só pode referenciar o `.Contracts` de outro módulo — nunca o `Domain`/`Application`/`Infrastructure` de outro módulo
- Só `PetAdoption.Api` (Host) referencia a `Infrastructure` de todos os módulos
- Cobertas por `PetAdoption.ArchitectureTests`

## Banco de dados

- Um único Postgres, cada módulo com seu próprio `DbContext` apontando para um schema separado (`identity`, `petpublishing`, `interestmatching`)
- Nenhum módulo faz join direto na tabela de outro. Leituras que combinam dados de módulos diferentes (ex: lista de interessados com foto do perfil) são compostas na camada de `Application`, com duas consultas simples — nunca via evento

## Eventos

- Domain event: interno a um contexto, tratado na mesma transação
- Integration event: cruza a fronteira entre contextos, tratado de forma eventualmente consistente, via RabbitMQ + Wolverine
- Padrão **Outbox** obrigatório: evento gravado na mesma transação da mudança do agregado — nunca publicar direto no bus dentro do handler
- Padrão **Inbox** do lado do consumidor para deduplicar (RabbitMQ entrega "pelo menos uma vez") — essencial para `InterestCreated` (incrementar contador não é idempotente por natureza)

## O que evitar

- Nunca acessar diretamente o repositório/agregado de outro módulo
- Nunca publicar um evento de integração fora da transação Outbox
- Não extrair `Pet` como entidade separada de `Listing` a não ser que surja necessidade real de reaproveitar o mesmo pet em mais de uma publicação
- Não commitar sem rodar CSharpier

## Decisões ainda em aberto (não implementar sem alinhar antes)

- Moderação de conteúdo (publicações falsas/abusivas)
- Geolocalização para a "vitrine de proximidade" (API de mapas/geocoding)
- Provedor de e-mail transacional (para o futuro módulo Notifications)
- Observabilidade/logging em produção

## Comportamento esperado nas sessões

- Sempre apresentar o plano de implementação (modo Plan) antes de tocar em código, e esperar aprovação
