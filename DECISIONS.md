# Architecture Decision Records

## ADR-001: IAnthropicClientWrapper leaks SDK types into AgentBase
**Status:** Accepted (known debt)  
**Context:** AgentBase reads MessageResponse.ToolCalls, .Content, .Usage, .StopReason — 
all Anthropic.SDK types. Tests use reflection to construct CommonFunction instances with 
private setters. The fix is an AgentTurn domain record + ACL adapter pattern.  
**Trigger for fixing:** Adding a second LLM provider (OpenAI, Bedrock) or an SDK version 
upgrade that breaks the reflection helper.
