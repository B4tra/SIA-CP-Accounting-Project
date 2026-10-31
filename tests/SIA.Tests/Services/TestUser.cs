using Moq;
using SIA.Application.Common.Interfaces;

namespace SIA.Tests.Services;

/// <summary>Helper test service: membuat mock <see cref="ICurrentUserService"/> untuk role tertentu.</summary>
internal static class TestUser
{
    /// <summary>User login dengan role yang diberikan (tanpa role = tidak punya akses apa pun).</summary>
    public static ICurrentUserService InRole(params string[] roles)
    {
        var mock = new Mock<ICurrentUserService>();
        mock.SetupGet(u => u.IsAuthenticated).Returns(roles.Length > 0);
        mock.SetupGet(u => u.UserId).Returns("user-test");
        mock.SetupGet(u => u.UserName).Returns("tester@sia.local");
        mock.Setup(u => u.IsInRole(It.IsAny<string>())).Returns<string>(r => roles.Contains(r));
        return mock.Object;
    }
}
