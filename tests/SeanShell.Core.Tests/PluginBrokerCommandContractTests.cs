using SeanShell.PluginBroker.Protocol;

namespace SeanShell.Core.Tests;

[TestClass]
public sealed class PluginBrokerCommandContractTests
{
    [TestMethod]
    public void BoundedDataOnlyContractIsAccepted()
    {
        var query = new PluginBrokerCommandQuery("docker", 12);
        PluginBrokerCommandDescriptor[] commands =
        [
            new(
                "docker.refresh",
                "Refresh containers",
                null,
                ["docker", "refresh"]),
        ];
        var digest = PluginBrokerCommandContract.ComputeCommandSetDigest(commands);
        var invocation = new PluginBrokerCommandInvocation(
            commands[0].Id,
            digest);
        var result = new PluginBrokerCommandResult(
            PluginBrokerCommandContract.SucceededOutcome,
            "Refreshed.");

        Assert.IsNull(PluginBrokerCommandContract.Validate(query));
        Assert.IsNull(PluginBrokerCommandContract.Validate(commands));
        Assert.IsNull(PluginBrokerCommandContract.Validate(invocation));
        Assert.IsNull(PluginBrokerCommandContract.Validate(result));
    }

    [TestMethod]
    public void QueryRejectsOversizedControlTextAndExcessResults()
    {
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandQuery(
                new string('q', PluginBrokerCommandContract.MaximumQueryCharacters + 1),
                1)));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandQuery("line\nbreak", 1)));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandQuery(
                "query",
                PluginBrokerCommandContract.MaximumCommandCount + 1)));
    }

    [TestMethod]
    public void DescriptorRejectsCaseInsensitiveDuplicateIds()
    {
        PluginBrokerCommandDescriptor[] commands =
        [
            new("repo.open", "Open", null, []),
            new("REPO.OPEN", "Open again", null, []),
        ];

        var error = PluginBrokerCommandContract.Validate(commands);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "invalid entry");
    }

    [TestMethod]
    public void DescriptorRejectsUnsafeIdAndControlText()
    {
        PluginBrokerCommandDescriptor[] unsafeId =
        [
            new("run powershell.exe", "Run", null, []),
        ];
        PluginBrokerCommandDescriptor[] unsafeText =
        [
            new("safe.id", "First\rSecond", null, []),
        ];

        Assert.IsNotNull(PluginBrokerCommandContract.Validate(unsafeId));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(unsafeText));
    }

    [TestMethod]
    public void DescriptorRejectsDuplicateOrExcessKeywords()
    {
        PluginBrokerCommandDescriptor[] duplicate =
        [
            new("safe.id", "Safe", null, ["Git", "git"]),
        ];
        PluginBrokerCommandDescriptor[] excess =
        [
            new(
                "safe.id",
                "Safe",
                null,
                Enumerable.Range(
                    0,
                    PluginBrokerCommandContract.MaximumKeywordCount + 1)
                    .Select(static index => $"keyword{index}")
                    .ToArray()),
        ];

        Assert.IsNotNull(PluginBrokerCommandContract.Validate(duplicate));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(excess));
    }

    [TestMethod]
    public void DescriptorSetRejectsAggregateTextOverflow()
    {
        var commands = Enumerable.Range(
                0,
                PluginBrokerCommandContract.MaximumCommandCount)
            .Select(index => new PluginBrokerCommandDescriptor(
                $"command.{index}",
                new string('t', PluginBrokerCommandContract.MaximumTitleCharacters),
                new string('s', PluginBrokerCommandContract.MaximumSubtitleCharacters),
                Enumerable.Range(0, PluginBrokerCommandContract.MaximumKeywordCount)
                    .Select(keyword => new string(
                        (char)('a' + keyword),
                        PluginBrokerCommandContract.MaximumKeywordCharacters))
                    .ToArray()))
            .ToArray();

        var error = PluginBrokerCommandContract.Validate(commands);

        Assert.IsNotNull(error);
        StringAssert.Contains(error, "text limit");
    }

    [TestMethod]
    public void DigestIsOrderIndependentButContentBound()
    {
        var first = new PluginBrokerCommandDescriptor(
            "a.command",
            "A",
            null,
            ["first"]);
        var second = new PluginBrokerCommandDescriptor(
            "b.command",
            "B",
            "Subtitle",
            []);

        var original = PluginBrokerCommandContract.ComputeCommandSetDigest(
            [first, second]);
        var reordered = PluginBrokerCommandContract.ComputeCommandSetDigest(
            [second, first]);
        var changed = PluginBrokerCommandContract.ComputeCommandSetDigest(
            [first with { Title = "Changed" }, second]);

        Assert.AreEqual(original, reordered);
        Assert.AreNotEqual(original, changed);
        Assert.IsTrue(PluginBrokerProtocol.IsValidSha256(original));
    }

    [TestMethod]
    public void InvocationRequiresSafeIdAndExactDigestShape()
    {
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandInvocation(
                "unsafe id",
                new string('A', 64))));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandInvocation(
                "safe.id",
                "not-a-digest")));
    }

    [TestMethod]
    public void ResultAllowsOnlyKnownOutcomeAndBoundedDisplayMessage()
    {
        Assert.IsNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandResult(
                PluginBrokerCommandContract.CancelledOutcome,
                null)));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandResult("execute-shell", null)));
        Assert.IsNotNull(PluginBrokerCommandContract.Validate(
            new PluginBrokerCommandResult(
                PluginBrokerCommandContract.FailedOutcome,
                new string(
                    'm',
                    PluginBrokerCommandContract.MaximumResultMessageCharacters + 1))));
    }

    [TestMethod]
    public void ContractDtosExposeNoExecutableObjectProperties()
    {
        Type[] dtoTypes =
        [
            typeof(PluginBrokerCommandQuery),
            typeof(PluginBrokerCommandDescriptor),
            typeof(PluginBrokerCommandInvocation),
            typeof(PluginBrokerCommandResult),
        ];

        foreach (var property in dtoTypes.SelectMany(
                     static type => type.GetProperties()))
        {
            Assert.IsFalse(typeof(Delegate).IsAssignableFrom(property.PropertyType));
            Assert.AreNotEqual(typeof(Type), property.PropertyType);
            Assert.AreNotEqual(typeof(nint), property.PropertyType);
        }
    }

    [TestMethod]
    public void StrictCodecRoundTripsEveryBoundedPayload()
    {
        var query = new PluginBrokerCommandQuery("git", 8);
        PluginBrokerCommandDescriptor[] descriptors =
        [
            new("git.refresh", "Refresh repositories", null, ["git"]),
        ];
        var invocation = new PluginBrokerCommandInvocation(
            "git.refresh",
            PluginBrokerCommandContract.ComputeCommandSetDigest(descriptors));
        var result = new PluginBrokerCommandResult(
            PluginBrokerCommandContract.SucceededOutcome,
            null);

        Assert.AreEqual(
            query,
            PluginBrokerCommandCodec.DeserializeQuery(
                PluginBrokerCommandCodec.Serialize(query)));
        var decodedDescriptors = PluginBrokerCommandCodec.DeserializeDescriptors(
            PluginBrokerCommandCodec.Serialize(descriptors));
        Assert.HasCount(1, decodedDescriptors);
        Assert.AreEqual(descriptors[0].Id, decodedDescriptors[0].Id);
        Assert.AreEqual(descriptors[0].Title, decodedDescriptors[0].Title);
        Assert.AreEqual(descriptors[0].Subtitle, decodedDescriptors[0].Subtitle);
        CollectionAssert.AreEqual(
            descriptors[0].Keywords,
            decodedDescriptors[0].Keywords);
        Assert.AreEqual(
            invocation,
            PluginBrokerCommandCodec.DeserializeInvocation(
                PluginBrokerCommandCodec.Serialize(invocation)));
        Assert.AreEqual(
            result,
            PluginBrokerCommandCodec.DeserializeResult(
                PluginBrokerCommandCodec.Serialize(result)));
    }

    [TestMethod]
    public void StrictCodecRejectsUnknownAndInvalidPayloads()
    {
        Assert.ThrowsExactly<System.Text.Json.JsonException>(
            () => PluginBrokerCommandCodec.DeserializeQuery(
                """{"query":"git","maximumResults":8,"shell":"cmd.exe"}"""));
        Assert.ThrowsExactly<InvalidDataException>(
            () => PluginBrokerCommandCodec.DeserializeInvocation(
                """{"commandId":"unsafe id","commandSetSha256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"}"""));
        Assert.ThrowsExactly<InvalidDataException>(
            () => PluginBrokerCommandCodec.DeserializeResult(
                new string('x', PluginBrokerProtocol.MaximumFrameCharacters + 1)));
    }
}
