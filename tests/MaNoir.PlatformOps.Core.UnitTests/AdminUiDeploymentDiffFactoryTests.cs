using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MaNoir.PlatformOps.Core;

namespace MaNoir.PlatformOps.Core.UnitTests;

[TestClass]
public sealed class AdminUiDeploymentDiffFactoryTests
{
	[TestMethod]
	public void Create_ShouldClassifyAddedRemovedChangedAndUnchangedContributions()
	{
		AdminUiDeploymentProjection previous = new AdminUiDeploymentProjection()
		{
			PluginId = "sarah",
			PluginVersion = "2.3.0",
			Contributions =
			[
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.dashboard",
					Label = "Dashboard",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Dashboard", RelativePath = "/admin", EffectivePath = "/home-automation/admin" }
					]
				},
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.settings",
					Label = "Settings",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Settings", RelativePath = "/settings", EffectivePath = "/home-automation/settings" }
					]
				},
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.legacy",
					Label = "Legacy",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Legacy", RelativePath = "/legacy", EffectivePath = "/home-automation/legacy" }
					]
				}
			]
		};

		AdminUiDeploymentProjection current = new AdminUiDeploymentProjection()
		{
			PluginId = "sarah",
			PluginVersion = "2.4.0",
			Contributions =
			[
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.dashboard",
					Label = "Dashboard",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Dashboard", RelativePath = "/admin", EffectivePath = "/home-automation/admin" }
					]
				},
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.settings",
					Label = "Settings",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Settings", RelativePath = "/configuration", EffectivePath = "/home-automation/configuration" }
					]
				},
				new AdminUiContributionDeploymentProjection()
				{
					ContributionId = "sarah.admin.reports",
					Label = "Reports",
					Domain = "Sarah",
					Pages =
					[
						new AdminUiPageDeploymentProjection() { Category = "Home", Name = "Reports", RelativePath = "/reports", EffectivePath = "/home-automation/reports" }
					]
				}
			]
		};

		AdminUiDeploymentDiff diff = AdminUiDeploymentDiffFactory.Create(previous, current);

		CollectionAssert.AreEqual(new[] { "sarah.admin.reports" }, diff.AddedContributionIds.ToArray());
		CollectionAssert.AreEqual(new[] { "sarah.admin.legacy" }, diff.RemovedContributionIds.ToArray());
		CollectionAssert.AreEqual(new[] { "sarah.admin.settings" }, diff.ChangedContributionIds.ToArray());
		CollectionAssert.AreEqual(new[] { "sarah.admin.dashboard" }, diff.UnchangedContributionIds.ToArray());
	}
}