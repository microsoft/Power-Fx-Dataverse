# IRSnapshot Guide Simplified

## How to Understand IRSnapShots?

### Reading IRSnapShots

- The **file name** of an IRSnapshot matches the **test name** it represents. For example, a file named `DistinctDelegation.txt` is associated with tests under `DistinctDelegationAsync`.
- Each IRSnapshot is connected to a specific test through an **ID parameter**. This ID corresponds to a line number in the `.txt` file, linking directly to the test.

## Adding a New Delegation Test

### Steps to Follow

1. **Create a new test** within the theory, ensuring that the ID increases sequentially. Remember, each ID must be unique and not repeated.
2. Set the `_regenerate` flag to **true** within the `DelegationTestUtility` class. This indicates that a new IRSnapshot needs to be generated.
3. **Run the test**. This action will generate a new IRSnapshot and save it in a `.txt` file.
4. After the snapshot is generated, set the `_regenerate` flag back to **false**. This indicates that the generation process is complete and no further snapshots need to be generated at this time.