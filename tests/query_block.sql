select "EventId", "Status", "RetryCount", "IsSpam", "ActionTaken", "DecisionReason"
from "EventStates"
where "ActorId" = 'user-block-2'
order by "UpdatedAt" desc
limit 10;

select *
from "BlacklistedUsers"
where "UserId" = 'user-block-2' and "PageId" = '1100652266458019';

select r."EventId", r."Reason", r."IsReviewed"
from "ReviewQueueItems" r
join "EventStates" e on e."EventId" = r."EventId"
where e."ActorId" = 'user-block-2'
order by r."QueuedAt" desc
limit 10;
