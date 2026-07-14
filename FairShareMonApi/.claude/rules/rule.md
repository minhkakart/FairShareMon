# Human Confirmation Policy

## Core Principle

The agent MUST NOT make decisions on behalf of the user when information is missing, ambiguous, conflicting, subjective, or requires preference-based judgment.

If a task cannot be completed with high confidence from the available context, the agent MUST stop and request clarification from the user before proceeding.

---

## Mandatory Clarification Triggers

The agent MUST ask the user for confirmation whenever any of the following conditions exist:

### 1. Missing Context

Required information is not available.

Examples:

* Missing requirements
* Missing constraints
* Missing acceptance criteria
* Missing environment details
* Missing business rules

The agent MUST ask for the missing information rather than inventing it.

---

### 2. Ambiguous Intent

Multiple reasonable interpretations exist.

Examples:

* A request can be implemented in several ways
* A requirement is unclear
* The desired outcome is not explicitly defined

The agent MUST present the possible interpretations and ask the user to choose.

---

### 3. Subjective or Preference-Based Decisions

The correct choice depends on user preferences.

Examples:

* Architecture choices
* Naming conventions
* UI/UX decisions
* Technology selection
* Coding style preferences
* Trade-offs between performance, maintainability, cost, security, or simplicity

The agent MUST explain the available options and request a decision from the user.

---

### 4. Assumption Required

The agent would need to assume facts that are not explicitly stated.

The agent MUST NOT:

* Guess
* Infer hidden requirements
* Create fictional context
* Select arbitrary defaults

Instead, it MUST ask the user.

---

### 5. Multiple Valid Solutions

When several valid approaches exist and no selection criteria have been provided.

The agent MUST:

1. Present the available options.
2. Explain the trade-offs.
3. Ask the user which option should be used.

---

### 6. Potentially Irreversible or High-Impact Actions

Before performing actions that could significantly affect systems, data, costs, security, or architecture, the agent MUST obtain explicit confirmation.

Examples:

* Data deletion
* Schema changes
* Infrastructure changes
* Security-related modifications
* Breaking API changes

---

## Prohibited Behavior

The agent MUST NOT:

* Silently choose a solution when user input is required.
* Invent requirements.
* Invent business rules.
* Assume preferences.
* Select defaults without informing the user.
* Continue execution after detecting ambiguity.

---

## Required Behavior

When clarification is needed:

1. Stop further implementation.
2. Clearly identify the uncertainty.
3. Explain why the decision matters.
4. Present available options when possible.
5. Ask concise questions.
6. Wait for the user's response before proceeding.

---

## Decision Rule

When uncertain:

**Ask first. Do not assume.**

The cost of asking an extra question is lower than the cost of implementing the wrong solution.


# Change Planning and Work Log Policy

## Purpose

Every feature, enhancement, bug fix, refactor, migration, or significant change MUST be documented in a planning log before implementation begins.

The planning log serves as the source of truth for:

* Requirements
* Decisions
* Scope
* Implementation progress
* Final outcomes

---

## Planning Directory

All planning documents MUST be stored under:

```text
/planning
```

If the directory does not exist, the agent MUST create it.

---

## File Naming Convention

Each work item MUST have its own Markdown file.

Format:

```text
/planning/[main-purpose].md
```

Examples:

```text
/planning/user-authentication.md
/planning/order-export-feature.md
/planning/refactor-cache-layer.md
/planning/fix-signalr-token-validation.md
```

File names MUST:

* Use lowercase
* Use kebab-case
* Be descriptive
* Represent the primary purpose of the work

---

## Mandatory Creation

Before making code changes, the agent MUST:

1. Determine the primary purpose of the work.
2. Create a planning file if one does not already exist.
3. Record the requested change.
4. Record assumptions and open questions.
5. Begin implementation only after documentation has been created.

No implementation should begin without a corresponding planning file.

---

## Required Planning Template

Every planning file MUST contain the following sections.

# Title

A short description of the work.

## Objective

Describe the requested change.

## Background

Relevant context and existing system behavior.

## Requirements

List all known requirements.

* Requirement 1
* Requirement 2

## Open Questions

Questions requiring clarification.

* Question 1
* Question 2

## Assumptions

Explicit assumptions currently being made.

* Assumption 1
* Assumption 2

## Implementation Plan

Step-by-step implementation approach.

1. Step 1
2. Step 2
3. Step 3

## Impact Analysis

Affected areas:

* APIs
* Database
* Infrastructure
* UI
* Services
* Documentation

## Progress Log

Chronological record of work.

### YYYY-MM-DD HH:mm

* Started planning.

### YYYY-MM-DD HH:mm

* Implemented X.

### YYYY-MM-DD HH:mm

* Updated Y.

## Final Outcome

Summary of completed work.

## Future Improvements

Optional future enhancements.

---

## Ongoing Updates

The agent MUST update the planning file whenever:

* New requirements are discovered
* Scope changes
* Decisions are made
* Implementation steps are completed
* Problems are encountered
* Work is finished

The planning file MUST remain synchronized with the actual implementation.

---

## Decision Documentation

All significant decisions MUST be recorded.

Example:

```markdown
## Decision Log

### Decision
Use Redis distributed cache.

### Reason
Application will run across multiple instances.

### Alternatives Considered
- In-memory cache
- Database cache
```

---

## Completion Requirement

Before considering a task complete, the agent MUST:

1. Update the Progress Log.
2. Update the Final Outcome section.
3. Record any remaining limitations.
4. Ensure the planning file accurately reflects the final implementation.

A task is not complete until its planning document has been updated.
