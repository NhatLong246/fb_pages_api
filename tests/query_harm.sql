select "EventId", "Status", "RetryCount", "IsSpam", "SpamSeverity", "ActionTaken", "DecisionReason"
from "EventStates"
where "ActorId" = 'user-harm-2'
order by "UpdatedAt" desc
limit 3;

select *
from "ReviewQueueItems"
where "EventId" = 'facebook:comment:1100652266458019_101_1778213055954';
