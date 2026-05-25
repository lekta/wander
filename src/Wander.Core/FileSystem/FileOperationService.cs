namespace Wander.Core.FileSystem;

public sealed class FileOperationService {
    private readonly IFileSystem _fs;


    public FileOperationService(IFileSystem fs) {
        _fs = fs;
    }

    public FileOperationService() : this(ServiceLocator.Get<IFileSystem>()) {
    }


    public void Copy(string source, string destination, bool overwrite = false) {
        if (_fs.DirectoryExists(source)) {
            _fs.CopyDirectory(source, destination, overwrite);
            return;
        }

        if (_fs.FileExists(source)) {
            _fs.CopyFile(source, destination, overwrite);
            return;
        }

        throw new FileNotFoundException("Source not found", source);
    }

    public void Move(string source, string destination) {
        if (!_fs.FileExists(source) && !_fs.DirectoryExists(source)) {
            throw new FileNotFoundException("Source not found", source);
        }

        _fs.MoveEntry(source, destination);
    }

    public void Delete(string path) {
        if (_fs.DirectoryExists(path)) {
            _fs.DeleteDirectory(path, recursive: true);
            return;
        }

        if (_fs.FileExists(path)) {
            _fs.DeleteFile(path);
            return;
        }

        throw new FileNotFoundException("Path not found", path);
    }

    public void Rename(string path, string newName) {
        if (string.IsNullOrWhiteSpace(newName)) {
            throw new ArgumentException("New name cannot be empty", nameof(newName));
        }

        _fs.Rename(path, newName);
    }

    public void CreateFolder(string parent, string name) {
        var path = Path.Combine(parent, name);
        _fs.CreateDirectory(path);
    }
}
