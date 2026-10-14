using SIA.Application.Common.Constants;

namespace SIA.Tests.Application;

public class PoliciesTests
{
    [Fact]
    public void RoleMap_MencakupSemuaPolicy()
    {
        var expected = new[]
        {
            Policies.RequireAdmin, Policies.ManageTransactions, Policies.ApprovePurchaseOrder,
            Policies.InputJournal, Policies.ViewReports, Policies.ViewAllData,
        };

        Assert.Equal(expected.OrderBy(x => x), Policies.RoleMap.Keys.OrderBy(x => x));
    }

    [Theory]
    [InlineData(Policies.RequireAdmin, new[] { Roles.Admin })]
    [InlineData(Policies.ApprovePurchaseOrder, new[] { Roles.Admin })]
    [InlineData(Policies.ManageTransactions, new[] { Roles.Admin, Roles.Staff })]
    [InlineData(Policies.InputJournal, new[] { Roles.Admin, Roles.Viewer })]
    [InlineData(Policies.ViewReports, new[] { Roles.Admin, Roles.Viewer })]
    [InlineData(Policies.ViewAllData, new[] { Roles.Admin, Roles.Staff, Roles.Viewer })]
    public void RoleMap_SesuaiMatriksHakAkses(string policy, string[] expectedRoles)
    {
        Assert.Equal(expectedRoles.OrderBy(x => x), Policies.RoleMap[policy].OrderBy(x => x));
    }

    [Fact]
    public void Staff_TidakBolehApprovePo_JurnalAtauLaporan()
    {
        Assert.DoesNotContain(Roles.Staff, Policies.RoleMap[Policies.ApprovePurchaseOrder]);
        Assert.DoesNotContain(Roles.Staff, Policies.RoleMap[Policies.InputJournal]);
        Assert.DoesNotContain(Roles.Staff, Policies.RoleMap[Policies.ViewReports]);
        Assert.DoesNotContain(Roles.Staff, Policies.RoleMap[Policies.RequireAdmin]);
    }

    [Fact]
    public void Viewer_TidakBolehMengelolaTransaksi()
    {
        Assert.DoesNotContain(Roles.Viewer, Policies.RoleMap[Policies.ManageTransactions]);
        Assert.DoesNotContain(Roles.Viewer, Policies.RoleMap[Policies.ApprovePurchaseOrder]);
    }

    [Fact]
    public void SemuaRoleDiRoleMap_AdalahRoleValid()
    {
        var all = Policies.RoleMap.Values.SelectMany(r => r).Distinct();
        Assert.All(all, role => Assert.Contains(role, Roles.All));
    }
}
