-- RetryUtils: Utility functions for retry logic and polling
-- Demonstrates: while loops, repeat/until, do/end blocks, nested local functions

local RetryUtils = {} :: RetryUtils

-- Retry an operation with exponential backoff
-- Demonstrates: while/do loop
function RetryUtils:RetryWithBackoff(operation: () -> (boolean, any), maxAttempts: number): (boolean, any)
    local attempt = 0
    local delay = 0.1
    local success = false
    local result = nil

    while attempt < maxAttempts do
        success, result = operation()
        if success then
            return true, result
        end
        attempt += 1
        delay *= 2
        task.wait(delay)
    end

    return false, result
end

-- Wait until a condition is met or timeout
-- Demonstrates: repeat/until loop
function RetryUtils:WaitForCondition(condition: () -> boolean, timeoutSeconds: number): boolean
    local elapsed = 0
    local interval = 0.1

    repeat
        if condition() then
            return true
        end
        task.wait(interval)
        elapsed += interval
    until elapsed >= timeoutSeconds

    return false
end

-- Process items in batches with isolation
-- Demonstrates: standalone do/end block for scope isolation
function RetryUtils:ProcessBatch(items: { any }, batchSize: number): { any }
    local results = {}

    for i = 1, #items, batchSize do
        do
            local batchResults = {}
            local batchEnd = math.min(i + batchSize - 1, #items)

            for j = i, batchEnd do
                table.insert(batchResults, items[j])
            end

            table.insert(results, batchResults)
        end
    end

    return results
end

-- Build a debounced version of a function
-- Demonstrates: nested local function
local function createDebouncer(delaySeconds: number): (callback: () -> ()) -> ()
    local lastCallTime = 0

    local function shouldExecute(): boolean
        local now = os.clock()
        if now - lastCallTime >= delaySeconds then
            lastCallTime = now
            return true
        end
        return false
    end

    return function(callback: () -> ())
        if shouldExecute() then
            callback()
        end
    end
end

-- Poll a resource until ready
-- Demonstrates: while with break, nested local function with return type
function RetryUtils:PollUntilReady(checkFn: () -> boolean, maxPolls: number): boolean
    local pollCount = 0

    local function incrementAndCheck(): boolean
        pollCount += 1
        return pollCount <= maxPolls
    end

    while incrementAndCheck() do
        if checkFn() then
            return true
        end
        task.wait(0.5)
    end

    return false
end

return RetryUtils
