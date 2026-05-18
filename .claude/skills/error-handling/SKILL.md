---
name: error-handling
description: Используй при реализации обработки ошибок для непредвиденных сбоев, ошибок аутентификации, ошибок «не найдено» и нарушений бизнес-правил — предоставляет full-stack обработку ошибок с ProblemDetails и правильными HTTP статус-кодами (проект)
---

# Full-Stack обработка ошибок

Все ошибки в DrimAgents должны обрабатываться консистентно с использованием формата ProblemDetails на бэкенде, BFF и фронтенде.

## Когда использовать

Используй этот скилл когда:

- Обрабатываешь исключения на бэкенде (доменные ошибки, инфраструктурные сбои)
- Реализуешь ответы на ошибки аутентификации/авторизации
- Обрабатываешь сценарии «не найдено»
- Управляешь нарушениями бизнес-правил
- Создаёшь error boundaries на фронтенде
- Реализуешь логику повторных попыток или восстановления после ошибок

## Типы ошибок и HTTP статус-коды

| Тип ошибки | Статус-код | Когда использовать |
|------------|-------------|-------------|
| Ошибка валидации | 400 | Невалидный пользовательский ввод (см. скилл validation) |
| Не авторизован | 401 | Пользователь не аутентифицирован |
| Запрещено | 403 | Пользователь аутентифицирован, но не имеет прав |
| Не найдено | 404 | Ресурс не существует |
| Конфликт | 409 | Конфликт состояния (например, дубликат уникального поля) |
| Необрабатываемая сущность | 422 | Нарушение бизнес-правила |
| Внутренняя ошибка сервера | 500 | Непредвиденная серверная ошибка |

## Соглашение о кодах ошибок

Паттерн: `domain:entity:operation:error_type`

**Примеры:**

- `blog:post:delete:not_found` — пост для удаления не существует
- `blog:post:publish:not_draft` — нельзя опубликовать пост, который не является черновиком
- `users:profile:update:forbidden` — пользователь не может обновить чужой профиль
- `skills:skill:learn:already_learning` — пользователь уже изучает этот навык
- `courses:enrollment:create:course_full` — курс достиг максимума записей

## Руководства по реализации

Подробные паттерны и примеры смотри в:

- **[Обработка ошибок на бэкенде](backend-errors.md)** — доменные исключения, GlobalExceptionHandler, интеграция с ProblemDetails, ошибки БД
- **[Обработка ошибок на фронтенде](frontend-errors.md)** — класс ApiError, error boundaries, логика повторных попыток, обработка ошибок в компонентах
- **[Тестирование](error-testing.md)** — тестирование сценариев ошибок, статус-кодов, кодов ошибок

## Краткий справочник

### Доменные исключения на бэкенде

```csharp
public class NotFoundException : DomainException
{
    public NotFoundException(string message, string errorCode)
        : base(message, errorCode, StatusCodes.Status404NotFound)
    {
    }
}

if (post == null)
{
    throw new NotFoundException(
        $"Post with ID {request.PostId} not found",
        "blog:post:delete:not_found");
}
```

### Ответ ProblemDetails на бэкенде

```json
{
  "type": "https://tools.ietf.org/html/rfc7807",
  "title": "Not Found",
  "status": 404,
  "detail": "Post with ID 12345 not found",
  "instance": "/api/posts/12345",
  "errorCode": "blog:post:delete:not_found",
  "traceId": "00-abc123..."
}
```

### BFF Passthrough

```typescript
const response = await fetch(`${process.env.BACKEND_URL}/api/posts/${id}`, {
  method: 'DELETE',
  headers: {
    'Authorization': `Bearer ${session.accessToken}`,
    'X-User-Id': session.user.id
  }
});

return new Response(await response.text(), {
  status: response.status,
  headers: { 'Content-Type': 'application/json' }
});
```

### ApiError на фронтенде

```typescript
try {
  await api.delete(`/api/posts/${postId}`);
  toast.success('Post deleted successfully');
} catch (error) {
  if (error instanceof ApiError) {
    if (error.isNotFound) {
      toast.error('Post not found');
    } else if (error.isForbidden) {
      toast.error('You do not have permission to delete this post');
    } else {
      toast.error(error.message);
    }
  }
}
```

### Error Boundary на фронтенде

```typescript
<ErrorBoundary fallback={<ErrorPage />}>
  {children}
</ErrorBoundary>
```

## Чеклист

Перед завершением реализации обработки ошибок:

**Бэкенд:**

- [ ] Классы доменных исключений созданы (NotFoundException, ForbiddenException и т.д.)
- [ ] Коды ошибок следуют соглашению `domain:entity:operation:error_type`
- [ ] GlobalExceptionHandler зарегистрирован и конвертирует исключения в ProblemDetails
- [ ] Ошибки «не найдено» бросают NotFoundException с 404
- [ ] Ошибки авторизации бросают ForbiddenException с 403
- [ ] Нарушения бизнес-правил бросают UnprocessableEntityException с 422
- [ ] Ошибки БД обработаны (уникальные ограничения, FK нарушения)
- [ ] Ошибки логируются с соответствующим уровнем
- [ ] Компонентные тесты проверяют правильные статус-коды и коды ошибок

**BFF:**

- [ ] ProblemDetails передаются без изменений от бэкенда
- [ ] BFF-специфичные ошибки (auth, недоступность бэкенда) возвращают ProblemDetails
- [ ] Сетевые ошибки обработаны грамотно (503)

**Фронтенд:**

- [ ] Класс ApiError создан для типизированной обработки ошибок
- [ ] API-клиент бросает ApiError с ProblemDetails
- [ ] Обработка ошибок в компонентах проверяет типы и коды ошибок
- [ ] Toast-уведомления показывают понятные пользователю сообщения
- [ ] Error boundary ловит непредвиденные ошибки рендеринга
- [ ] Глобальная страница ошибки (error.tsx) реализована
- [ ] Страница «не найдено» (not-found.tsx) реализована
- [ ] Коды ошибок маппятся на i18n-сообщения (если применимо)
