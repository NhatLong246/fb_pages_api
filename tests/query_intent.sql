select "ActorId", "EventId", "Status", "IsSpam", "Intent", "Sentiment", "ActionTaken"
from "EventStates"
where "ActorId" in ('user-intent-4', 'user-intent-5', 'user-intent-6')
order by "UpdatedAt" desc;
