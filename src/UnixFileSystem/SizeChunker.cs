﻿using Ipfs.CoreApi;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Ipfs.Engine.UnixFileSystem
{
    /// <summary>
    ///   Chunks a data stream into data blocks based upon a size.
    /// </summary>
    public class SizeChunker
    {
        /// <summary>
        ///   Performs the chunking.
        /// </summary>
        /// <param name="stream">
        ///   The data source.
        /// </param>
        /// <param name="options">
        ///   The options when adding data to the IPFS file system.
        /// </param>
        /// <param name="blockService">
        ///   The destination for the chunked data block(s).
        /// </param>
        /// <param name="cancel">
        ///   Is used to stop the task.  When cancelled, the <see cref="TaskCanceledException"/> is raised.
        /// </param>
        /// <returns>
        ///    A task that represents the asynchronous operation. The task's value is
        ///    the sequence of file system nodes of the added data blocks.
        /// </returns>
        public async Task<IEnumerable<FileSystemNode>> ChunkAsync(
            Stream stream, 
            AddFileOptions options, 
            IBlockApi blockService, 
            CancellationToken cancel)
        {
            var nodes = new List<FileSystemNode> ();
            var chunkSize = options.ChunkSize; // TODO: Upper limit for DOS attacks.
            var chunk = new byte[chunkSize];
            var chunking = true;

            while (chunking)
            {
                // Get an entire chunk.
                int length = 0;
                while (length < chunkSize)
                {
                    var n = await stream.ReadAsync(chunk, length, chunkSize - length, cancel);
                    if (n < 1)
                    {
                        chunking = false;
                        break;
                    }
                    length += n;
                }

                //  Only generate empty block, when the stream is empty.
                if (length == 0 && nodes.Count > 0)
                {
                    chunking = false;
                    break;
                }

                if (options.RawLeaves)
                {
                    // TODO: Inefficent to copy chunk, use ArraySegment in DataMessage.Data
                    var data = new byte[length];
                    Array.Copy(chunk, data, length);
                    var cid = await blockService.PutAsync(
                        data: data,
                        contentType: "raw",
                        multiHash: options.Hash,
                        pin: options.Pin,
                        cancel: cancel);
                    nodes.Add(new FileSystemNode
                    {
                        Id = cid,
                        Size = length,
                        DagSize = length,
                        Links = FileSystemLink.None
                    });
                }
                else
                {
                    // Build the DAG.
                    var dm = new DataMessage
                    {
                        Type = DataType.File,
                        FileSize = (ulong)length,
                    };
                    if (length > 0)
                    {
                        // TODO: Inefficent to copy chunk, use ArraySegment in DataMessage.Data
                        var data = new byte[length];
                        Array.Copy(chunk, data, length);
                        dm.Data = data;
                    }
                    var pb = new MemoryStream();
                    ProtoBuf.Serializer.Serialize<DataMessage>(pb, dm);
                    var dag = new DagNode(pb.ToArray(), null, options.Hash);

                    // Save it.
                    dag.Id = await blockService.PutAsync(
                        data: dag.ToArray(),
                        multiHash: options.Hash,
                        pin: options.Pin,
                        cancel: cancel);

                    var node = new FileSystemNode
                    {
                        Id = dag.Id,
                        Size = length,
                        DagSize = dag.Size,
                        Links = FileSystemLink.None
                    };
                    nodes.Add(node);
                }
            }

            return nodes;
        }
    }
}