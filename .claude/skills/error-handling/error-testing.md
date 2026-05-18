# Тестирование обработки ошибок

## Компонентные тесты на бэкенде

```csharp
[Fact]
public async Task Should_return_404_when_post_not_found()
{
    // Arrange
    var (client, _) = await _fixture.CreateAuthedHttpClient();

    // Act
    var response = await client.DeleteAsync("/api/posts/999999");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.NotFound);

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    problem.ShouldNotBeNull();
    problem.Status.Should().Be(404);
    problem.Title.Should().Be("Not Found");
    problem.Detail.Should().Contain("not found");

    var errorCode = problem.Extensions["errorCode"]?.ToString();
    errorCode.Should().Be("blog:post:delete:not_found");
}

[Fact]
public async Task Should_return_403_when_user_not_owner()
{
    // Arrange
    var (client, account) = await _fixture.CreateAuthedHttpClient();

    var otherUser = CreateUser(email: "other@example.com");
    var post = CreatePost(authorId: otherUser.Id);
    await _fixture.Database.Save(otherUser, post);

    // Act
    var response = await client.DeleteAsync($"/api/posts/{post.Id}");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    var errorCode = problem.Extensions["errorCode"]?.ToString();
    errorCode.Should().Be("blog:post:delete:forbidden");
}

[Fact]
public async Task Should_return_422_when_deleting_published_post()
{
    // Arrange
    var (client, account) = await _fixture.CreateAuthedHttpClient();

    var post = CreatePost(authorId: account.Id, isPublished: true);
    await _fixture.Database.Save(post);

    // Act
    var response = await client.DeleteAsync($"/api/posts/{post.Id}");

    // Assert
    response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

    var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
    var errorCode = problem.Extensions["errorCode"]?.ToString();
    errorCode.Should().Be("blog:post:delete:is_published");
}
```

## Тесты фронтенда

```typescript
// __tests__/DeletePostButton.test.tsx
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { DeletePostButton } from '@/components/DeletePostButton';

describe('DeletePostButton', () => {
  it('shows error message when post not found', async () => {
    const user = userEvent.setup();

    global.fetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 404,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc7807',
        title: 'Not Found',
        status: 404,
        detail: 'Post with ID 123 not found',
        errorCode: 'blog:post:delete:not_found'
      })
    });

    render(<DeletePostButton postId="123" />);

    const deleteButton = screen.getByRole('button', { name: /delete/i });
    await user.click(deleteButton);

    global.confirm = jest.fn(() => true);

    await waitFor(() => {
      expect(screen.getByText('Post not found')).toBeInTheDocument();
    });
  });

  it('shows error message when user forbidden', async () => {
    const user = userEvent.setup();

    global.fetch = jest.fn().mockResolvedValue({
      ok: false,
      status: 403,
      json: async () => ({
        type: 'https://tools.ietf.org/html/rfc7807',
        title: 'Forbidden',
        status: 403,
        detail: 'You do not have permission to delete this post',
        errorCode: 'blog:post:delete:forbidden'
      })
    });

    render(<DeletePostButton postId="123" />);

    const deleteButton = screen.getByRole('button', { name: /delete/i });
    await user.click(deleteButton);

    global.confirm = jest.fn(() => true);

    await waitFor(() => {
      expect(screen.getByText('You do not have permission to delete this post'))
        .toBeInTheDocument();
    });
  });
});
```
