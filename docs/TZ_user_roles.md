# ТЗ: роли и статусы пользователя (User.Role, User.Status)

## Исходное требование

Деактивация/активация людей сейчас доступна всем из UI — так быть не должно. Нужны роли по образцу
GitHub (основатель, админ, разработчик, участник, читатель) и отдельный от роли статус пользователя.
Деактивировать человека может только основатель (Owner), даже не админ.

Область ТЗ — все слои: Domain → Infrastructure → Application → Api → Client. Аутентификации в API
по-прежнему нет, поэтому «кто действует» (actor) передаётся явно и потом заменится на identity из токена.

## Принятые решения

- **Роль одна на пользователя и глобальная для workspace.** Ролей на проект (как GitHub repo permissions)
  пока нет — вводить, когда появятся несколько workspace или закрытые проекты.
- **Порядок enum = сила роли**, чтобы сравнивать `actor.Role >= UserRole.Admin`.
- **Owner может быть несколько, но минимум один.** Последнего Owner нельзя понизить или деактивировать.
- **Статус отделён от роли**: `Invited` (создан, ещё не заходил), `Active`, `Deactivated`.
  Заменяет `IsActive`/`DeactivatedAt`. `Invited` пока выставляется при создании и переводится в `Active`
  первым же изменением профиля самим человеком (когда появится вход — при первом входе).
- **Права проверяются в Application** (`IPermissionService`), а не в контроллере и не в домене: домен знает
  инварианты про Owner, но не знает, кто такой actor. Нарушение прав → `403 Forbidden`.
- **Actor до появления auth** — заголовок `X-Actor-Id: <guid>`; отсутствие заголовка → 401.
  Клиент шлёт `AppState.CurrentUserId` («Это я»). Это временная схема, она уйдёт вместе с JWT.

## Роли (`UserRole`)

| Значение | Роль | Кто это | Может |
|---|---|---|---|
| `0` | **Reader** — читатель | заказчик, наблюдатель | читать проекты, задачи, людей; редактировать свой профиль |
| `1` | **Member** — участник | продакт, тестировщик | + создавать задачи; редактировать/менять статус **своих** задач (создатель или исполнитель); назначать себя |
| `2` | **Developer** — разработчик | делает работу | + редактировать/удалять любые задачи, назначать любого исполнителя, менять статусы |
| `3` | **Admin** — админ | руководитель | + создавать/переименовывать/удалять проекты; добавлять людей; редактировать чужие профили; менять роли **ниже Admin** |
| `4` | **Owner** — основатель | создатель workspace | + назначать/снимать Owner и Admin; деактивировать/активировать людей |

## Статусы (`UserStatus`)

| Значение | Статус | Смысл |
|---|---|---|
| `0` | `Invited` | создан, ещё не работал; назначать можно |
| `1` | `Active` | работает |
| `2` | `Deactivated` | ушёл; назначать нельзя, история сохраняется |

## Матрица действий с людьми

| Действие | Owner | Admin | Developer/Member/Reader |
|---|---|---|---|
| Добавить человека (роль по умолчанию `Member`) | ✓ | ✓ | — |
| Сменить роль (цель и новая роль ниже роли actor) | ✓ | ✓ | — |
| Назначить/снять Owner или Admin | ✓ | — | — |
| Деактивировать / активировать | ✓ | — | — |
| Редактировать чужой профиль (имя, должность, ссылки…) | ✓ | ✓ | — |
| Менять чужой username/email | ✓ | — | — |
| Редактировать свой профиль | ✓ | ✓ | ✓ |
| Понизить себя | ✓ (если не последний Owner) | ✓ | — |
| Повысить себя | — | — | — |

## Матрица действий с проектами и задачами

| Действие | Reader | Member | Developer | Admin+ |
|---|---|---|---|---|
| Читать проекты/задачи | ✓ | ✓ | ✓ | ✓ |
| Создать задачу | — | ✓ | ✓ | ✓ |
| Редактировать/удалять/менять статус задачи | — | своей | ✓ | ✓ |
| Назначить исполнителя | — | себя | ✓ | ✓ |
| Создать/переименовать/удалить проект | — | — | — | ✓ |

«Своя задача» для Member: `TaskItem.CreatedById == actor` или `AssigneeId == actor`. Для этого в `TaskItem`
добавляется `CreatedById : Guid?` (nullable: старые задачи созданы до ролей).

## Доменные инварианты (`Flow.Domain`)

- `User.Role : UserRole` (по умолчанию `Member`), `User.Status : UserStatus`, `User.StatusChangedAt : DateTime?`.
- `ChangeRole(UserRole role)` — просто меняет роль; проверка «последний Owner» делается в Application, т.к. домен
  не видит других пользователей (как с уникальностью username).
- `Deactivate()` бросает `InvalidOperationException`, если `Role == Owner` (сначала передать владение) или уже `Deactivated`.
- `Activate()` бросает, если уже `Active`/`Invited`.
- `MarkActive()` — `Invited → Active`; из `Deactivated` не переводит.
- `CanBeAssigned => Status != Deactivated`.
- `IsActive` остаётся как вычисляемое (`Status != Deactivated`) — чтобы не ломать `TaskAssignCommandHandler` и клиент.

## Application

- `IPermissionService` (`Flow.Application/Abstractions`): `EnsureCanManageUsers(actor)`, `EnsureCanChangeRole(actor, target, newRole)`,
  `EnsureCanDeactivate(actor)`, `EnsureCanEditProfile(actor, target)`, `EnsureCanEditTask(actor, task)`, `EnsureCanManageBoards(actor)`.
  Нарушение → `ForbiddenException` (новое исключение в Application) → `403` в контроллере.
- Все изменяющие команды получают `ActorId : Guid` первым параметром. Actor загружается через `IUserRepository`;
  не найден или `Deactivated` → 401.
- `UserChangeRoleCommand(ActorId, UserId, Role)`: проверка прав + «последний Owner» (`IUserRepository.CountByRoleAsync(Owner)`).
- `UserDeactivateCommand`/`UserActivateCommand` — только Owner.
- `UserCreateCommand` принимает `Role` (по умолчанию `Member`; Admin не может создать Owner/Admin).
- `IUserRepository.CountByRoleAsync(UserRole, ct)`.

## Infrastructure

- Миграция `AddUserRoleAndStatus`: колонки `Role int`, `Status int`, `StatusChangedAt timestamptz null`;
  удалить `IsActive`, `DeactivatedAt`. Данные: `Status = Deactivated` где был `IsActive = false`, иначе `Active`;
  `StatusChangedAt = DeactivatedAt`; `Role = Member` всем, кроме самого раннего по `CreatedAt` — ему `Owner`
  (если пользователей нет — Owner получит первый созданный: в `UserCreateCommand` первый пользователь workspace = Owner).
- `TaskItems.CreatedById uuid null`, FK → Users (Restrict), индекс.
- Интеграционные тесты: миграция на базе с пользователями, инвариант «первый — Owner».

## Shared / Api

- `UserResponse`: `+ Role : UserRole`, `+ Status : UserStatus`, `+ StatusChangedAt`; `IsActive` остаётся (вычисляемое) для совместимости.
- `CreateUserRequest`: `+ Role : UserRole?`.
- `ChangeUserRoleRequest(UserRole Role)` → `PATCH /users/{id}/role`.
- `TaskResponse`: `+ CreatedById : Guid?`.
- Заголовок `X-Actor-Id` читается в фильтре/middleware `ActorProvider` (`IActorAccessor` в Application),
  контроллеры не разбирают заголовок сами. Ответы: `401` нет/неизвестный actor, `403` нет прав.
- Enum'ы `UserRole`, `UserStatus` в `Flow.Shared/Contracts/Users` — зеркала доменных (Shared не ссылается на Domain).

## Client

- Чип роли в списке людей и в шапке профиля (`Owner` — акцентный, `Admin` — sage, остальные нейтральные).
- Селект роли на странице профиля: виден Owner/Admin, варианты ограничены правами actor.
- Кнопки «Деактивировать/Активировать» — только у Owner; «Добавить» — только Owner/Admin; кнопки проектов/задач — по матрице.
- `FlowApi` добавляет `X-Actor-Id` из `AppState.CurrentUserId`; без выбранного «Это я» изменяющие действия недоступны
  (подсказка «выберите свой профиль»).
- Пустое состояние «Люди»: первый созданный человек становится Owner — сказать об этом в модалке.

## Тесты

- Domain: `Deactivate` Owner бросает; `MarkActive` только из `Invited`; `IsActive` по статусу.
- Application: матрица прав — по одному тесту на строку таблиц выше (Fakes); последний Owner не понижается;
  Admin не может выдать Admin; actor `Deactivated` → 401.
- Infrastructure: миграция данных, `CountByRoleAsync`.
- Client: ручная проверка сценариев «Owner деактивирует», «Admin видит селект без Owner», «Member не видит удаление проекта».

## Вне этого ТЗ

- Аутентификация (JWT/OAuth): заменит `X-Actor-Id`, `Invited → Active` при первом входе, приглашения по email.
- Роли на проект.
- Аудит смены ролей (кто и когда) — после комментариев/истории задачи.

## Подзадачи

1. Domain: `UserRole`, `UserStatus`, `User.Role/Status`, `TaskItem.CreatedById` + тесты.
2. Infrastructure: миграция `AddUserRoleAndStatus`, `CountByRoleAsync`, интеграционные тесты.
3. Application: `IActorAccessor`, `IPermissionService`, `ForbiddenException`, `ActorId` в командах, `UserChangeRole`, тесты матрицы.
4. Shared/Api: enum'ы, `PATCH /users/{id}/role`, `X-Actor-Id` → 401/403.
5. Client: чипы и селект ролей, показ кнопок по правам, `X-Actor-Id` из «Это я».

Порядок: 1 → 2 → 3 → 4 → 5.
