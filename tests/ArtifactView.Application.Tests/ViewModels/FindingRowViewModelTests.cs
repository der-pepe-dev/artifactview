using ArtifactView.Application.ViewModels;
using ArtifactView.Core.Models;
using System.Threading.Tasks;

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

    [Test]
    public async Task Forwards_finding_properties()
    {
        var finding = MakeFinding(interpretation: "likely tampered", provenance: "core.analyzer.foo");
        var vm = new FindingRowViewModel(finding);

        await Assert.That(vm.Category).IsEqualTo("Test");
        await Assert.That(vm.Observation).IsEqualTo("Some observation");
        await Assert.That(vm.Interpretation).IsEqualTo("likely tampered");
        await Assert.That(vm.Provenance).IsEqualTo("core.analyzer.foo");
        await Assert.That(vm.Finding).IsSameReferenceAs(finding);
    }

    [Test]
    public async Task IsExpanded_defaults_to_false()
    {
        var vm = new FindingRowViewModel(MakeFinding());
        await Assert.That(vm.IsExpanded).IsFalse();
    }

    [Test]
    public async Task IsExpanded_raises_PropertyChanged()
    {
        var vm = new FindingRowViewModel(MakeFinding());
        string? changedProp = null;
        vm.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        vm.IsExpanded = true;

        await Assert.That(changedProp).IsEqualTo(nameof(vm.IsExpanded));
    }

    [Test]
    public async Task HasDetails_false_when_nothing_extra()
    {
        // Unknown confidence, no factors, no provenance.
        var vm = new FindingRowViewModel(MakeFinding());
        await Assert.That(vm.HasDetails).IsFalse();
    }

    [Test]
    public async Task HasDetails_true_when_provenance_set()
    {
        var vm = new FindingRowViewModel(MakeFinding(provenance: "core.analyzer.jpeg"));
        await Assert.That(vm.HasDetails).IsTrue();
        await Assert.That(vm.HasProvenance).IsTrue();
    }

    [Test]
    public async Task HasDetails_true_when_interpretation_confidence_known()
    {
        var vm = new FindingRowViewModel(MakeFinding(interpretationConfidence: new ConfidenceScore(70)));
        await Assert.That(vm.HasDetails).IsTrue();
        await Assert.That(vm.HasInterpretationConfidence).IsTrue();
    }

    [Test]
    public async Task HasDetails_true_when_supporting_factors_present()
    {
        var vm = new FindingRowViewModel(MakeFinding(supportingFactors: ["Factor A"]));
        await Assert.That(vm.HasDetails).IsTrue();
        await Assert.That(vm.HasSupportingFactors).IsTrue();
    }

    [Test]
    public async Task HasDetails_true_when_conflicting_factors_present()
    {
        var vm = new FindingRowViewModel(MakeFinding(conflictingFactors: ["Factor B"]));
        await Assert.That(vm.HasDetails).IsTrue();
        await Assert.That(vm.HasConflictingFactors).IsTrue();
    }

    [Test]
    public async Task HasInterpretationConfidence_false_for_unknown()
    {
        var vm = new FindingRowViewModel(MakeFinding(interpretationConfidence: ConfidenceScore.Unknown));
        await Assert.That(vm.HasInterpretationConfidence).IsFalse();
    }

    [Test]
    public async Task HasSupportingFactors_false_for_empty_list()
    {
        var vm = new FindingRowViewModel(MakeFinding(supportingFactors: []));
        await Assert.That(vm.HasSupportingFactors).IsFalse();
    }

    [Test]
    public async Task HasConflictingFactors_false_for_empty_list()
    {
        var vm = new FindingRowViewModel(MakeFinding(conflictingFactors: []));
        await Assert.That(vm.HasConflictingFactors).IsFalse();
    }

    [Test]
    public async Task Toggle_expands_and_collapses()
    {
        var vm = new FindingRowViewModel(MakeFinding(provenance: "src"));
        await Assert.That(vm.IsExpanded).IsFalse();

        vm.IsExpanded = true;
        await Assert.That(vm.IsExpanded).IsTrue();

        vm.IsExpanded = false;
        await Assert.That(vm.IsExpanded).IsFalse();
    }
}