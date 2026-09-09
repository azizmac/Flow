# ТЗ: доменная сущность User (пользователь)

## Исходное требование

Спроектировать пользователя таск-трекера:
- Id в формате GUID.
- Имя, фамилия, имя пользователя (username).
- Данные, которые нужны в трекере, чтобы назначать пользователя на задачу, упоминать его
  и показывать карточку с полной информацией по клику: ссылка на GitHub, соцсети, номер телефона и т.п.

Область этого ТЗ — **только доменный класс** в `Flow.Domain` и его юнит-тесты. Персистентность,
Application-фичи, API и привязка пользователя к задаче описаны в разделе «Следующие шаги» и делаются отдельно.

## Принятые допущения и решения

- **User — это профиль, а не учётная запись.** Пароли, токены, роли и способ входа (Identity / OAuth)
  в сущности нет намеренно: аутентификация будет отдельным контекстом, который ссылается на `User.Id`.
  Так домен не зависит от способа логина.
- **`Username` — для упоминаний (`@ilya`) и для URL профиля.** Нормализуется в нижний регистр,
  уникален глобально (индекс в БД делается на этапе Infrastructure). Формат — `^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$`
  (2–32 символа, латиница/цифры/`. _ -`, не начинается и не заканчивается на разделитель).
- **`Email` обязателен** — это единственный стабильный контактный канал и будущий ключ для приглашений
  и входа. Нормализуется в нижний регистр, уникален глобально.
- **Имя и фамилия обязательны**, `FullName` вычисляется как `"{FirstName} {LastName}"` — отдельное
  редактируемое поле «отображаемое имя» не вводим, чтобы не было двух источников правды.
- **Внешние ссылки вынесены в коллекцию `UserLink`** (тип + URL), а не в набор колонок
  `GitHubUrl`, `TelegramUrl`, ... — иначе каждая новая соцсеть = миграция. Типы перечислены в `UserLinkType`
  (`GitHub`, `GitLab`, `Telegram`, `LinkedIn`, `Website`, `Other`). **Не более одной ссылки каждого типа**;
  повторное добавление того же типа заменяет URL. Ссылка — только абсолютный `http`/`https` URL.
- **Телефон опционален**, хранится в формате E.164 (`+79991234567`). Перед проверкой из ввода
  вырезаются пробелы, дефисы и скобки; формат — `^\+[1-9]\d{6,14}$`.
- **Деактивация вместо удаления.** Пользователь, который когда-либо был назначен на задачу или упомянут,
  не может исчезнуть из истории. Поэтому `Deactivate()` ставит `IsActive = false` и `DeactivatedAt`;
  физическое удаление пользователя доменом не предусмотрено. Деактивированного пользователя нельзя
  назначать на новые задачи (проверка будет в Application при назначении).
- **Все поля `{ get; private set; }`**, мутации только через методы (как в `Board`/`TaskItem`).
  Конструктор приватный, единственная точка создания — `User.Create(...)`.
- **Даты — `DateTime.UtcNow`**, как в остальных сущностях.

## Поля `User`

| Поле | Тип | Обяз. | Ограничение | Назначение |
|---|---|---|---|---|
| `Id` | `Guid` | да | PK | Технический ключ, на него ссылаются задачи/комментарии |
| `Username` | `string` | да | 2–32, `^[a-z0-9][a-z0-9._-]{0,30}[a-z0-9]$`, lower, unique | Упоминания `@username`, URL профиля |
| `Email` | `string` | да | ≤ 254, содержит `@`, lower, unique | Контакт, приглашения, будущий вход |
| `FirstName` | `string` | да | 1–100, trim | Имя |
| `LastName` | `string` | да | 1–100, trim | Фамилия |
| `FullName` | `string` | — | вычисляемое | `"Имя Фамилия"` для карточек и списков назначаемых |
| `AvatarUrl` | `string?` | нет | ≤ 500, абсолютный http(s) | Аватар в карточке задачи / упоминании |
| `JobTitle` | `string?` | нет | ≤ 100 | Должность («Backend-разработчик») |
| `Bio` | `string?` | нет | ≤ 1000 | Короткое «о себе» на карточке |
| `PhoneNumber` | `string?` | нет | E.164 `^\+[1-9]\d{6,14}$` | Контакт |
| `Links` | `IReadOnlyCollection<UserLink>` | — | ≤ 1 на тип | GitHub, Telegram, LinkedIn, сайт и т.п. |
| `IsActive` | `bool` | да | — | Можно ли назначать на задачи |
| `CreatedAt` | `DateTime` | да | UTC | Дата регистрации |
| `DeactivatedAt` | `DateTime?` | нет | UTC | Когда деактивирован |

### `UserLink`

| Поле | Тип | Описание |
|---|---|---|
| `Type` | `UserLinkType` | `GitHub`, `GitLab`, `Telegram`, `LinkedIn`, `Website`, `Other` |
| `Url` | `string` | Абсолютный `http`/`https` URL, ≤ 500 символов |

Создаётся только через `User.SetLink(type, url)`.

## Методы `User`

| Метод | Поведение |
|---|---|
| `Create(username, email, firstName, lastName)` | Фабрика. Валидирует и нормализует все четыре поля, `IsActive = true`, `CreatedAt = UtcNow` |
| `ChangeUsername(username)` | Нормализация + проверка формата |
| `ChangeEmail(email)` | Нормализация + проверка формата |
| `ChangeName(firstName, lastName)` | Оба поля обязательны, trim |
| `ChangeAvatar(url?)` | `null` — убрать аватар; иначе проверка URL |
| `ChangeJobTitle(jobTitle?)` | `null`/пусто — очистить; иначе trim + длина |
| `ChangeBio(bio?)` | `null`/пусто — очистить; иначе trim + длина |
| `ChangePhoneNumber(phone?)` | `null`/пусто — очистить; иначе нормализация в E.164 + проверка |
| `SetLink(type, url)` | Добавить ссылку или заменить существующую того же типа |
| `RemoveLink(type)` | Удалить ссылку типа; если её нет — ничего не делать |
| `Deactivate()` | `IsActive = false`, `DeactivatedAt = UtcNow`; повторный вызов — `InvalidOperationException` |
| `Activate()` | `IsActive = true`, `DeactivatedAt = null`; повторный вызов — `InvalidOperationException` |

Уникальность `Username`/`Email` **между** пользователями домен проверить не может (нет доступа к хранилищу) —
это делается в Application через репозиторий (по аналогии с `IBoardRepository.ExistsByKeyAsync`) и
страхуется уникальными индексами в БД.

## Ошибки

Как и в остальном домене: неверный ввод → `ArgumentException`, неверное состояние
(повторная деактивация) → `InvalidOperationException`. Никаких `Result`/`DomainException`.

## Тесты (`Flow.Domain.Tests/UserTests.cs`)

- `Create` нормализует username/email в нижний регистр и обрезает пробелы в имени.
- `Create` бросает на пустых/невалидных username, email, имени, фамилии.
- `ChangePhoneNumber` принимает `+7 (999) 123-45-67` и сохраняет `+79991234567`; бросает на `12345`.
- `SetLink` того же типа заменяет URL, а не добавляет вторую ссылку; бросает на относительном URL и на `ftp://`.
- `RemoveLink` отсутствующего типа — не бросает.
- `Deactivate`/`Activate` меняют флаг и дату; повторный вызов бросает `InvalidOperationException`.
- `FullName` = `"Имя Фамилия"`.

## Следующие шаги (вне этого ТЗ)

1. **Infrastructure:** `UserConfiguration` (unique `Username`, unique `Email`, `Links` как owned-коллекция
   в таблице `UserLinks` с уникальным `(UserId, Type)`), миграция `AddUsers`.
2. **Application:** `IUserRepository` (`GetByIdAsync`, `ExistsByUsernameAsync`, `ExistsByEmailAsync`,
   `SearchAsync` для автодополнения `@`), фичи `Features/Users/{Commands,Queries}`.
3. **Shared/Api:** `UserResponse` (с `FullName`, `AvatarUrl`, `Links`), `POST/GET /users`, `GET/PATCH /users/{id}`.
4. **Назначение на задачу:** `TaskItem.AssigneeId : Guid?` + `TaskItem.Assign(userId)` / `Unassign()`,
   FK → `Users` с `Restrict`; в хендлере — проверка `IsActive`. Сделано (#13): `TaskResponse.AssigneeId`,
   `PATCH /tasks/{id}/assignee`, фильтр `?assigneeId=` в списке задач. Вложенная карточка `Assignee` в `TaskResponse`
   не добавлена — клиент берёт её через `GET /users/{id}`, чтобы не тянуть пользователей в каждый список задач.
5. **Упоминания:** парсинг `@username` в описании/комментариях — отдельная фича, требует комментариев к задаче.

## Совместимость с веткой `StronglyTypeId`

Ветка `origin/StronglyTypeId` вводит типизированные id (`Flow.Shared/Ids`: `BoardId` «boa», `TaskId` «tas», `StatusId` «sta»)
с EF value converter'ами и ручным парсингом в контроллерах. Пока она не в `main`, `User` реализован на `Guid`,
как остальной `main`. После мержа ветки перевод `User` на типизированный id делается отдельным коммитом по чек-листу:

1. `Flow.Shared/Ids/UserId.cs` — по образцу `BoardId`: префикс `"use"`, `[JsonConverter(typeof(TypedIdJsonConverter<UserId>))]`, `New()/Create()/TryParse()/Parse()`.
2. `Flow.Domain`: `User.Id : UserId = null!`, `UserLink.UserId : UserId = null!`; в конструкторе `UserId.New()`.
3. `Flow.Infrastructure/UserConfiguration`: `builder.Property(u => u.Id).HasConversion(id => id.Value, v => UserId.Create(v))`
   и то же для `links.Property(l => l.UserId)` внутри `OwnsMany` (составной ключ `(UserId, Type)` остаётся). Схема БД не меняется, миграция не нужна.
4. `Flow.Application`: `Guid UserId` → `UserId` во всех командах/запросах и в `IUserRepository`; `UserResponse.Id : UserId`.
5. `Flow.Api/UsersController`: маршруты `{id}` без `:guid`, `UserId.TryParse` → 400 с сообщением про формат `use_<guid>`,
   `CreatedAtAction(..., new { id = response.Id.ToString() }, ...)`.
6. Тесты: `TypedIdTests` — добавить `UserId` в round-trip; фейки и интеграционные тесты пересобрать по компилятору.
7. Если к тому моменту сделан п. 4 «Назначение на задачу» — `TaskItem.AssigneeId : UserId?` и конвертер в `TaskItemConfiguration`.

Конфликт при мерже ожидается в `AGENTS.md` (обе ветки правят раздел про Domain) — разрешается объединением обоих абзацев.
