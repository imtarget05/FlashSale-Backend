# Business & Technical Requirements

## Business Context
A retail enterprise runs frequent "Flash Sale" campaigns. During a flash sale, high-demand items are sold at a deep discount, but with strictly limited inventory (e.g., exactly 100 units). 

When the flash sale opens, marketing channels drive a massive spike in traffic (up to 5,000 concurrent purchase attempts within seconds).

## Key Business Requirements
1. **No Overselling (Strict Inventory Control)**: If inventory is 100, the system must sell exactly 100 or fewer units. Selling 101 units is a critical business failure.
2. **High Availability**: The system must remain responsive during the traffic spike. The database must not crash due to connection exhaustion.
3. **Fairness**: Orders should generally be processed in a first-come, first-served manner.
4. **Order Status Transparency**: Customers should receive prompt feedback on whether their order was accepted or if the item is sold out.

## Technical Constraints (Phase 1)
- The system must first be implemented in the simplest way possible (Synchronous API + Relational Database).
- We must intentionally observe the system's behavior and failures under concurrency before introducing advanced architectural patterns.
