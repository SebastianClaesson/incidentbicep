using System.Text.Json;
using Bicep.Extension.IncidentIo.Handlers;

namespace Bicep.Extension.IncidentIo.Tests;

public class EscalationPathTests
{
    private static EscalationPathTarget User(string id) => new() { Id = id, Type = EscalationTargetType.user, Urgency = EscalationUrgency.high };

    private static EscalationPathNode Level(string user) => new() { Level = new() { Targets = [User(user)], TimeToAckSeconds = 300 } };

    /// <summary>The Terraform docs example: branch on working hours, loop in hours, hand off out of hours.</summary>
    private static Dictionary<string, EscalationPathSequence> Example() => new()
    {
        ["main"] = new() { Nodes = [new() { Id = "start", Branch = new() { If = new() { WorkingHoursActive = "UK" }, Then = "in_hours", Else = "out_of_hours" } }] },
        ["in_hours"] = new() { Nodes = [Level("u1"), new() { Delay = new() { DelaySeconds = 120 } }, new() { Loop = new() { BackTo = "start", Times = 3 } }] },
        ["out_of_hours"] = new() { Nodes = [Level("u2"), new() { EscalationPath = new() { EscalationPathId = "01FALLBACK" } }] }
    };

    [Fact]
    public void Unflattens_To_The_Api_Tree()
    {
        var path = EscalationPathSequences.Unflatten("main", Example(), ["UK"]);

        var root = path[0]!;
        Assert.Equal("start", root["id"]!.GetValue<string>());
        Assert.Equal("if_else", root["type"]!.GetValue<string>());
        Assert.Equal("escalation.working_hours[\"UK\"]", root["if_else"]!["conditions"]![0]!["subject"]!.GetValue<string>());
        Assert.Equal("is_active", root["if_else"]!["conditions"]![0]!["operation"]!.GetValue<string>());

        var then = root["if_else"]!["then_path"]!.AsArray();
        Assert.Equal("in_hours/0", then[0]!["id"]!.GetValue<string>());           // derived id
        Assert.Equal("first", then[0]!["level"]!["ack_mode"]!.GetValue<string>()); // Terraform v7 default
        Assert.Equal("repeat", then[2]!["type"]!.GetValue<string>());
        Assert.Equal("start", then[2]!["repeat"]!["to_node"]!.GetValue<string>());

        var otherwise = root["if_else"]!["else_path"]!.AsArray();
        Assert.Equal("01FALLBACK", otherwise[1]!["escalation_path"]!["escalation_path_id"]!.GetValue<string>());
    }

    [Fact]
    public void Priority_Branch_Uses_One_Of()
    {
        var sequences = new Dictionary<string, EscalationPathSequence>
        {
            ["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1", "P2"] }, Then = "urgent" } }] },
            ["urgent"] = new() { Nodes = [Level("u1")] }
        };

        var condition = EscalationPathSequences.Unflatten("main", sequences, [])[0]!["if_else"]!["conditions"]![0]!;

        Assert.Equal("escalation.priority", condition["subject"]!.GetValue<string>());
        Assert.Equal("one_of", condition["operation"]!.GetValue<string>());
        Assert.Equal("P2", condition["param_bindings"]![0]!["array_value"]![1]!["literal"]!.GetValue<string>());
    }

    [Fact]
    public void Round_Trips_Through_The_Api_Shape()
    {
        var path = EscalationPathSequences.Unflatten("main", Example(), ["UK"]);
        using var doc = JsonDocument.Parse(path.ToJsonString());

        var (start, sequences) = EscalationPathSequences.Flatten(doc.RootElement);

        Assert.Equal("main", start);
        Assert.Equal(["in_hours", "main", "out_of_hours"], sequences.Keys.Order());
        Assert.Equal("in_hours", sequences["main"].Nodes[0].Branch!.Then);
        Assert.Equal("UK", sequences["main"].Nodes[0].Branch!.If.WorkingHoursActive);
        Assert.Null(sequences["in_hours"].Nodes[0].Id); // derived ids are not surfaced
        Assert.Equal("start", sequences["in_hours"].Nodes[2].Loop!.BackTo);
    }

    [Theory]
    [InlineData("nodesAfterBranch", "Nodes after a branch")]
    [InlineData("unknownSequence", "Unknown sequence")]
    [InlineData("branchToStart", "Start sequence is branched to")]
    [InlineData("cycle", "loops back on itself")]
    [InlineData("unreachable", "Unreachable sequence")]
    [InlineData("twoBlocks", "Node does more than one thing")]
    [InlineData("emptyNode", "Empty node")]
    [InlineData("slashInId", "Invalid node id")]
    [InlineData("duplicateId", "Duplicate node id")]
    [InlineData("badLoopTarget", "Cannot loop back to this node")]
    [InlineData("unknownLoopTarget", "Unknown node")]
    [InlineData("unknownWorkingHours", "declares no workingHours")]
    [InlineData("branchTestsTwoThings", "exactly one of")]
    [InlineData("badSequenceName", "Invalid sequence name")]
    [InlineData("sharedSequence", "branched to more than once")]
    public void Invalid_Paths_Are_Rejected(string scenario, string expectedMessage)
    {
        var s = new Dictionary<string, EscalationPathSequence>();
        var start = "main";
        switch (scenario)
        {
            case "nodesAfterBranch":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "a" } }, Level("u")] };
                s["a"] = new() { Nodes = [Level("u")] };
                break;
            case "unknownSequence":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "missing" } }] };
                break;
            case "branchToStart":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "main" } }] };
                break;
            case "cycle":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "a" } }] };
                s["a"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "b" } }] };
                s["b"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "a" } }] };
                break;
            case "unreachable":
                s["main"] = new() { Nodes = [Level("u")] };
                s["orphan"] = new() { Nodes = [Level("u")] };
                break;
            case "twoBlocks":
                s["main"] = new() { Nodes = [new() { Level = Level("u").Level, Delay = new() { DelaySeconds = 1 } }] };
                break;
            case "emptyNode":
                s["main"] = new() { Nodes = [new()] };
                break;
            case "slashInId":
                s["main"] = new() { Nodes = [new() { Id = "a/b", Level = Level("u").Level }] };
                break;
            case "duplicateId":
                s["main"] = new() { Nodes = [new() { Id = "x", Level = Level("u").Level }, new() { Id = "x", Level = Level("u").Level }] };
                break;
            case "badLoopTarget":
                s["main"] = new() { Nodes = [Level("u"), new() { Id = "mid", Delay = new() { DelaySeconds = 1 } }, new() { Loop = new() { BackTo = "mid", Times = 2 } }] };
                break;
            case "unknownLoopTarget":
                s["main"] = new() { Nodes = [Level("u"), new() { Loop = new() { BackTo = "nowhere", Times = 2 } }] };
                break;
            case "unknownWorkingHours":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { WorkingHoursActive = "US" }, Then = "a" } }] };
                s["a"] = new() { Nodes = [Level("u")] };
                break;
            case "branchTestsTwoThings":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { WorkingHoursActive = "UK", PriorityOneOf = ["P1"] }, Then = "a" } }] };
                s["a"] = new() { Nodes = [Level("u")] };
                break;
            case "badSequenceName":
                start = "1st";
                s["1st"] = new() { Nodes = [Level("u")] };
                break;
            case "sharedSequence":
                s["main"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P1"] }, Then = "a", Else = "b" } }] };
                s["a"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P2"] }, Then = "shared" } }] };
                s["b"] = new() { Nodes = [new() { Branch = new() { If = new() { PriorityOneOf = ["P3"] }, Then = "shared" } }] };
                s["shared"] = new() { Nodes = [Level("u")] };
                break;
        }

        var ex = Assert.Throws<IncidentIoException>(() => EscalationPathSequences.Unflatten(start, s, ["UK"]));
        Assert.Contains(expectedMessage, ex.Message);
    }

    [Fact]
    public void Loop_May_Be_Followed_By_A_Single_Handoff()
    {
        var s = new Dictionary<string, EscalationPathSequence>
        {
            ["main"] = new() { Nodes = [Level("u"), new() { Loop = new() { BackTo = "main/0", Times = 2 } }, new() { EscalationPath = new() { EscalationPathId = "01X" } }] }
        };

        var path = EscalationPathSequences.Unflatten("main", s, []);

        Assert.Equal(3, path.Count);
    }
}

public class ScheduleRotationTests
{
    private sealed class Probe : ScheduleRotationHandler
    {
        public bool UpToDate(string payload, string existing)
            => IsUpToDate(System.Text.Json.Nodes.JsonNode.Parse(payload)!.AsObject(), JsonDocument.Parse(existing).RootElement);
    }

    [Fact]
    public void Working_Intervals_Compare_As_A_Set()
    {
        var desired = """{"name":"r","working_intervals":[{"weekday":"monday","start_time":"09:00","end_time":"17:00"},{"weekday":"tuesday","start_time":"09:00","end_time":"17:00"}]}""";
        var actual = """{"name":"r","working_intervals":[{"weekday":"tuesday","start_time":"09:00","end_time":"17:00"},{"weekday":"monday","start_time":"09:00","end_time":"17:00"}]}""";

        Assert.True(new Probe().UpToDate(desired, actual));
    }

    [Fact]
    public void User_Order_Is_A_Change()
    {
        var desired = """{"name":"r","users":[{"id":"a"},{"id":"b"}]}""";
        var actual = """{"name":"r","users":[{"id":"b","name":"B"},{"id":"a","name":"A"}]}""";

        Assert.False(new Probe().UpToDate(desired, actual));
    }
}
