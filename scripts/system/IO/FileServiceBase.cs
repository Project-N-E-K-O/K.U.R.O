using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Kuros.Systems.IO
{
    /// <summary>
    /// 提供与游戏运行目录（exe 所在目录）及其子目录交互的基础工具。
    /// </summary>
    public abstract class FileServiceBase
    {
        protected string RootDirectory { get; }

        protected FileServiceBase()
        {
            RootDirectory = AppDomain.CurrentDomain.BaseDirectory;
        }

        protected string CombinePath(params string[] segments)
        {
            string path = RootDirectory;
            foreach (var segment in segments)
            {
                path = Path.Combine(path, segment);
            }

            return Path.GetFullPath(path);
        }

        protected void EnsureDirectory(string relativePath)
        {
            var fullPath = CombinePath(relativePath);
            if (!Directory.Exists(fullPath))
            {
                Directory.CreateDirectory(fullPath);
            }
        }

        protected bool FileExists(string relativePath)
        {
            return File.Exists(CombinePath(relativePath));
        }

        protected string ReadText(string relativePath, Encoding? encoding = null)
        {
            var fullPath = CombinePath(relativePath);
            return File.Exists(fullPath) ? File.ReadAllText(fullPath, encoding ?? Encoding.UTF8) : string.Empty;
        }

        protected byte[] ReadBytes(string relativePath)
        {
            var fullPath = CombinePath(relativePath);
            return File.Exists(fullPath) ? File.ReadAllBytes(fullPath) : Array.Empty<byte>();
        }

        protected void WriteText(string relativePath, string content, Encoding? encoding = null)
        {
            var fullPath = CombinePath(relativePath);
            EnsureDirectory(Path.GetDirectoryName(relativePath) ?? string.Empty);
            File.WriteAllText(fullPath, content, encoding ?? Encoding.UTF8);
        }

        protected void WriteBytes(string relativePath, byte[] data)
        {
            var fullPath = CombinePath(relativePath);
            EnsureDirectory(Path.GetDirectoryName(relativePath) ?? string.Empty);
            File.WriteAllBytes(fullPath, data);
        }

        protected async Task WriteTextAsync(string relativePath, string content, Encoding? encoding = null)
        {
            var fullPath = CombinePath(relativePath);
            EnsureDirectory(Path.GetDirectoryName(relativePath) ?? string.Empty);
            await File.WriteAllTextAsync(fullPath, content, encoding ?? Encoding.UTF8);
        }

        protected async Task WriteBytesAsync(string relativePath, byte[] data)
        {
            var fullPath = CombinePath(relativePath);
            EnsureDirectory(Path.GetDirectoryName(relativePath) ?? string.Empty);
            await File.WriteAllBytesAsync(fullPath, data);
        }
    }
}

