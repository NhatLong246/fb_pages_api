select "EventId", "Status", "RetryCount", "ActionTaken", "DecisionReason"
from "EventStates"
where "ActorId" = 'user-drift-1'
order by "ReceivedAt" asc;
