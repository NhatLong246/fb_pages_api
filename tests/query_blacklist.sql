\select "EventId", "Status", "RetryCount", "IsSpam", "ActionTaken", "DecisionReason"
from "EventStates"
where "ActorId" = 'user-blacklist-2'
order by "UpdatedAt" desc
limit 5;

select *
from "BlacklistedUsers"
where "UserId" = 'user-blacklist-2' and "PageId" = '1100652266458019';
