# Сравнение подходов к DbContext: FlowDbContext vs классический EF6-стиль

Повод: пример `ApplicationDbContext` из другого проекта (`DressyBackendLogic`), где `DbContext` написан в более старом стиле. Ниже — что отличается в `Flow.Infrastructure/Persistence/FlowDbContext.cs` и почему.

## 1. Конструктор

**Пример (другой проект):**
```csharp
public ApplicationDbContext() : base()
{
}

public ApplicationDbContext(DbContextOptions options)
    : base(options)
{
}
```

**FlowDbContext:**
```csharp
public sealed class FlowDbContext(DbContextOptions<FlowDbContext> options) : DbContext(options)
```

| | Другой проект | FlowDbContext |
|---|---|---|
| Тип параметра | `DbContextOptions` (не-generic) | `DbContextOptions<FlowDbContext>` (generic) |
| Параметрless-конструктор | Есть | Нет |

**Почему generic-параметр лучше.** `DbContextOptions<TContext>` однозначно привязывает опции к конкретному контексту. Это важно, если в одном DI-контейнере зарегистрировано несколько разных `DbContext` — не-generic `DbContextOptions` в таком случае может резолвиться неоднозначно. Так генерируют все актуальные шаблоны `dotnet new`/EF-скаффолдинг. У Flow пока один контекст, но так — на будущее и корректнее.

**Почему нет параметрless-конструктора.** Без `OnConfiguring` с захардкоженной строкой подключения `new ApplicationDbContext()` упадёт в рантайме с «no database provider configured» — то есть выглядит рабочим, но не является таковым. `dotnet ef migrations add ...` в Flow резолвит опции через DI стартап-проекта (`--startup-project src/Flow.Api`), отдельный конструктор для тулинга не требуется. Добавлять недействующий конструктор «на всякий случай» — мёртвый код.

## 2. DbSet-свойства

**Пример (другой проект):**
```csharp
public virtual DbSet<Brand> Brands { get; set; }
public virtual DbSet<Card> Cards { get; set; }
```

**FlowDbContext:**
```csharp
public DbSet<Board> Boards => Set<Board>();
public DbSet<Status> Statuses => Set<Status>();
public DbSet<TaskItem> TaskItems => Set<TaskItem>();
```

| | Другой проект | FlowDbContext |
|---|---|---|
| Сеттер | Публичный (`{ get; set; }`) | Нет (только `=>`) |
| `virtual` | Есть | Нет |
| Инициализация | EF присваивает свойство при старте (через сеттер) | `Set<TEntity>()` вызывается при каждом обращении, потокобезопасно |

**Это главное отличие, и оно осознанное:**

- `virtual` в EF Core имеет смысл только при `UseLazyLoadingProxies()` — EF на рантайме подменяет тип DbContext-сущности на динамический прокси-класс, который перехватывает обращения к `virtual`-навигациям и лениво их подгружает без явного `.Include(...)`.
- Lazy-loading прокси **требуют, чтобы entity-классы были не `sealed`**, а навигационные свойства — `virtual`. У Flow все сущности (`Board`, `Status`, `TaskItem`) — `sealed` (осознанно, чтобы никто не унаследовался и не обошёл инварианты через переопределение методов). Это делает lazy-loading прокси в принципе несовместимыми с текущим дизайном.
- Вместо ленивой подгрузки везде используется явный `.Include(...)` в репозиториях (см. `BoardRepository.GetByIdAsync`) — предсказуемо и без риска N+1-запросов, которым славится lazy loading.
- Публичный сеттер у `DbSet` — чистый footgun: снаружи `DbContext` никто и никогда не должен писать `dbContext.Boards = ...`, а сеттер формально такую возможность открывает. Get-only `=> Set<T>()` — текущая официальная рекомендация Microsoft (так выглядит в актуальных шаблонах и документации EF Core), сеттер там просто не нужен.

## 3. Namespace

Другой проект — блочный `namespace X { ... }`; Flow — file-scoped `namespace X;` (C# 10+). Чисто синтаксика, поведение идентично; file-scoped короче и это стиль всего остального кода в репозитории.

## 4. `OnModelCreating`

Совпадает: оба используют `modelBuilder.ApplyConfigurationsFromAssembly(...)` для применения всех `IEntityTypeConfiguration<T>` из сборки.

## Итог

| Критерий | Другой проект | FlowDbContext | Почему так у Flow |
|---|---|---|---|
| Параметр конструктора | `DbContextOptions` | `DbContextOptions<FlowDbContext>` | Однозначность при нескольких DbContext |
| Параметрless-конструктор | Есть | Нет | Не нужен — тулинг резолвит опции через DI |
| DbSet | `virtual { get; set; }` | `=> Set<T>()` | `sealed`-сущности + явный `.Include`, без lazy loading |
| Namespace | Блочный | File-scoped | Синтаксис C# 10+, стиль проекта |

Стиль из другого проекта не «неправильный» — это классический EF6/раннего EF Core паттерн, рабочий и сейчас. Разница — в том, что Flow целиком не использует lazy-loading прокси и держит сущности `sealed`, а под этот выбор текущий вариант `FlowDbContext` подходит лучше.
