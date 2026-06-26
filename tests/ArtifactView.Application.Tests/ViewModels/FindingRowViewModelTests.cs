using ArtifactView.Application.ViewModels;
using ArtifactView.Core.Models;
using Xunit;

namespace ArtifactView.Application.Tests.ViewModels;

public sealed class FindingRowViewModelTests
{
    private static Finding MakeFinding(
        string?              interpretation           = null,
        ConfidenceScore?     interpretationConfidence = null,
        string[]?            supportingFactors        = null,
        string[]?            conflictingFactors       = null,
        string?              provenance               = null)
    {
        return new Finding
        {
            Id                       = "test",
            Category                 = "Test",
            Observation              = "Some observation",
            Interpretation           = interpretation,
            InterpretationConfidence = interpretationConfidence ?? ConfidenceScore.Unknown,
            SupportingFactors        = supportingFactors ?? [],
            ConflictingFactors       = conflictingFactors ?? [],
            Provenance               = provenance
        };
    }

    [Fact]
    public void Forwards_finding_properties()
    {
        var finding = MakeFinding(interpretation: "likely tampered", provenance: "core.analyzer.foo");
        var vm = new FindingRowViewModel(finding);

        Assert.Equal("Test",             vm.Category);
        Assert.Equal("Some observation", vm.Observation);
        Assert.Equal("likely tampered",  vm.Interpretation);
        Assert.Equal("core.analyzer.foo", vm.Provenance);
        Assert.Same(finding, vm.Finding);
    }

    [Fact]
    public void IsExpanded_defaults_to_false()
    {
        var vm = new FindingRowViewModel(MakeFinding());
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void IsExpanded_raises_PropertyChanged()
    {
        var vm = new FindingRowViewModel(MakeFinding());
        string? changedProp = null;
        vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        vm.IsExpanded = true;

        Assert.Equal(nameof(vm.IsExpanded), changedProp);
    }

    [Fact]
    public void HasDetails_false_when_nothing_extra()
    {
        // Unknown confidence, no factors, no provenance.
        var vm = new FindingRowViewModel(MakeFinding());
        Assert.False(vm.HasDetails);
    }

    [Fact]
    public void HasDetails_true_when_provenance_set()
    {
        var vm = new FindingRowViewModel(MakeFinding(provenance: "core.analyzer.jpeg"));
        Assert.True(vm.HasDetails);
        Assert.True(vm.HasProvenance);
    }

    [Fact]
    public void HasDetails_true_when_interpretation_confidence_known()
    {
        var vm = new FindingRowViewModel(MakeFinding(interpretationConfidence: new ConfidenceScore(70)));
        Assert.True(vm.HasDetails);
        Assert.True(vm.HasInterpretationConfidence);
    }

    [Fact]
    public void HasDetails_true_when_supporting_factors_present()
    {
        var vm = new FindingRowViewModel(MakeFinding(supportingFactors: ["Factor A"]));
        Assert.True(vm.HasDetails);
        Assert.True(vm.HasSupportingFactors);
    }

    [Fact]
    public void HasDetails_true_when_conflicting_factors_present()
    {
        var vm = new FindingRowViewModel(MakeFinding(conflictingFactors: ["Factor B"]));
        Assert.True(vm.HasDetails);
        Assert.True(vm.HasConflictingFactors);
    }

    [Fact]
    public void HasInterpretationConfidence_false_for_unknown()
    {
        var vm = new FindingRowViewModel(MakeFinding(interpretationConfidence: ConfidenceScore.Unknown));
        Assert.False(vm.HasInterpretationConfidence);
    }

    [Fact]
    public void HasSupportingFactors_false_for_empty_list()
    {
        var vm = new FindingRowViewModel(MakeFinding(supportingFactors: []));
        Assert.False(vm.HasSupportingFactors);
    }

    [Fact]
    public void HasConflictingFactors_false_for_empty_list()
    {
        var vm = new FindingRowViewModel(MakeFinding(conflictingFactors: []));
        Assert.False(vm.HasConflictingFactors);
    }

    [Fact]
    public void Toggle_expands_and_collapses()
    {
        var vm = new FindingRowViewModel(MakeFinding(provenance: "src"));
        Assert.False(vm.IsExpanded);

        vm.IsExpanded = true;
        Assert.True(vm.IsExpanded);

        vm.IsExpanded = false;
        Assert.False(vm.IsExpanded);
    }
}
