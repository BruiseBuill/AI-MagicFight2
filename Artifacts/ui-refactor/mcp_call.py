import asyncio
import json
import sys
from pathlib import Path

from mcp import ClientSession
from mcp.client.streamable_http import streamable_http_client


async def main():
    async with streamable_http_client('http://127.0.0.1:8080/mcp') as (reader, writer):
        async with ClientSession(reader, writer) as session:
            await session.initialize()
            action = sys.argv[1]
            if action == 'resource':
                result = await session.read_resource(sys.argv[2])
            elif action == 'schema':
                result = await session.list_tools()
                for tool in result.tools:
                    if tool.name in sys.argv[2:]:
                        print(tool.name, tool.input_schema)
                return
            else:
                if action == 'code':
                    args = {'action': 'execute', 'code': Path(sys.argv[2]).read_text(encoding='utf-8')}
                    action = 'execute_code'
                else:
                    args = json.loads(sys.argv[2]) if len(sys.argv) > 2 else {}
                result = await session.call_tool(action, args)
            print(result.model_dump_json())


asyncio.run(main())
