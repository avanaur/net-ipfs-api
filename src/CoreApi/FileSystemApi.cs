﻿using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ipfs.Api
{

    /// <summary>
    ///   Manages the files/directories in IPFS.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///   This API is accessed via the <see cref="IpfsClient.FileSystem"/> property.
    ///   </para>
    /// </remarks>
    /// <seealso href="https://github.com/ipfs/interface-ipfs-core/tree/master/API/files">Files API</seealso>
    public class FileSystemApi
    {
        IpfsClient ipfs;
        Lazy<DagNode> emptyFolder;

        internal FileSystemApi(IpfsClient ipfs)
        {
            this.ipfs = ipfs;
            this.emptyFolder = new Lazy<DagNode>(() => ipfs.Object.NewDirectoryAsync().Result);
        }

        /// <summary>
        ///   Add a file to the interplanetary file system.
        /// </summary>
        /// <param name="path"></param>
        public async Task<FileSystemNode> AddFileAsync(string path)
        {
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var node = await AddAsync(stream, Path.GetFileName(path));
                return node;
            }
        }

        /// <summary>
        ///   Add some text to the interplanetary file system.
        /// </summary>
        /// <param name="text"></param>
        public Task<FileSystemNode> AddTextAsync(string text)
        {
            return AddAsync(new MemoryStream(Encoding.UTF8.GetBytes(text), false));
        }

        /// <summary>
        ///   Add a <see cref="Stream"/> to interplanetary file system.
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="name"></param>
        public async Task<FileSystemNode> AddAsync(Stream stream, string name = "")
        {
            var json = await ipfs.UploadAsync("add", stream);
            var r = JObject.Parse(json);
            return new FileSystemNode
            {
                Hash = (string)r["Hash"],
                Name = name,
                IpfsClient = ipfs
            };
        }

        /// <summary>
        ///   Add a directory and its files to the interplanetary file system.
        /// </summary>
        /// <param name="path"></param>
        /// <param name="recursive"></param>
        public async Task<FileSystemNode> AddDirectoryAsync(string path, bool recursive = true)
        {
            // Add the files and sub-directories.
            path = Path.GetFullPath(path);
            var files = Directory
                .EnumerateFiles(path)
                .Select(AddFileAsync);
            if (recursive)
            {
                var folders = Directory
                    .EnumerateDirectories(path)
                    .Select(dir => AddDirectoryAsync(dir, recursive));
                files = files.Union(folders);
            }
            var nodes = await Task.WhenAll(files);

            // Create the directory with links to the created files and sub-directories
            var links = nodes.Select(node => node.ToLink());
            var folder = emptyFolder.Value.AddLinks(links);
            var directory = await ipfs.Object.PutAsync(folder);

            return new FileSystemNode
            {
                Hash = directory.Hash,
                Name = Path.GetFileName(path),
                Links = links,
                IsDirectory = true,
                Size = 0,
                IpfsClient = ipfs
            };

        }

        /// <summary>
        ///   Reads the content of an existing IPFS file as text.
        /// </summary>
        /// <param name="path">
        ///   A path to an existing file, such as "QmXarR6rgkQ2fDSHjSY5nM2kuCXKYGViky5nohtwgF65Ec/about"
        ///   or "QmZTR5bcpQD7cFgTorqxZDYaew1Wqgfbd2ud9QqGPAkK2V"
        /// </param>
        /// <returns></returns>
        public async Task<String> ReadAllTextAsync(string path)
        {
            using (var data = await ReadFileAsync(path))
            using (var text = new StreamReader(data))
            {
                return await text.ReadToEndAsync();
            }
        }

        /// <summary>
        ///   Opens an existing IPFS file for reading.
        /// </summary>
        /// <param name="path">
        ///   A path to an existing file, such as "QmXarR6rgkQ2fDSHjSY5nM2kuCXKYGViky5nohtwgF65Ec/about"
        ///   or "QmZTR5bcpQD7cFgTorqxZDYaew1Wqgfbd2ud9QqGPAkK2V"
        /// </param>
        /// <returns>
        ///   A <see cref="Stream"/> to the file contents.
        /// </returns>
        public Task<Stream> ReadFileAsync(string path)
        {
            return ipfs.DownloadAsync("cat", path);
        }

        /// <summary>
        ///   Get information about the file or directory.
        /// </summary>
        /// <param name="path">
        ///   A path to an existing file or directory, such as "QmXarR6rgkQ2fDSHjSY5nM2kuCXKYGViky5nohtwgF65Ec/about"
        ///   or "QmZTR5bcpQD7cFgTorqxZDYaew1Wqgfbd2ud9QqGPAkK2V"
        /// </param>
        /// <returns></returns>
        public async Task<FileSystemNode> ListFileAsync(string path)
        {
            var json = await ipfs.DoCommandAsync("file/ls", path);
            var r = JObject.Parse(json);
            var hash = (string)r["Arguments"][path];
            var o = (JObject)r["Objects"][hash];
            var node = new FileSystemNode()
            {
                Hash = (string)o["Hash"],
                Size = (long)o["Size"],
                IsDirectory = (string)o["Type"] == "Directory",
                Links = new FileSystemLink[0]
            };
            var links = o["Links"] as JArray;
            if (links != null)
            {
                node.Links = links
                    .Select(l => new FileSystemLink()
                    {
                        Name = (string)l["Name"],
                        Hash = (string)l["Hash"],
                        Size = (long)l["Size"],
                        IsDirectory = (string)l["Type"] == "Directory",
                    })
                    .ToArray();
            }

            return node;
        }

    }
}
