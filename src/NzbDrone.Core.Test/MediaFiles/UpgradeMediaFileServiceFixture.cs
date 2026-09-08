using System.IO;
using System.Linq;
using FizzWare.NBuilder;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Disk;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.MediaFiles;
using NzbDrone.Core.MediaFiles.EpisodeImport;
using NzbDrone.Core.Parser.Model;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Core.Tv;
using NzbDrone.Test.Common;

namespace NzbDrone.Core.Test.MediaFiles
{
    public class UpgradeMediaFileServiceFixture : CoreTest<UpgradeMediaFileService>
    {
        private const string ParkedFileSuffix = ".krzw-upgrade-bak";

        private EpisodeFile _episodeFile;
        private LocalEpisode _localEpisode;

        [SetUp]
        public void Setup()
        {
            _localEpisode = new LocalEpisode();
            _localEpisode.Path = @"C:\Test\Unsorted\30.rock.s01e01.new.mkv".AsOsAgnostic();
            _localEpisode.Series = new Series
                                   {
                                       Path = @"C:\Test\TV\Series".AsOsAgnostic()
                                   };

            _episodeFile = Builder<EpisodeFile>
                .CreateNew()
                .Build();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localEpisode.Series.Path).FullName))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(It.IsAny<string>()))
                  .Returns(true);

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.GetParentFolder(It.IsAny<string>()))
                  .Returns<string>(c => Path.GetDirectoryName(c));

            // The replacement is transferred into place by the mover; return a file with a distinct
            // relative path so the service can compute NewFilePath.
            Mocker.GetMock<IMoveEpisodeFiles>()
                  .Setup(c => c.MoveEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>()))
                  .Returns(new EpisodeFile { RelativePath = @"Season 01\30.rock.s01e01.new.mkv" });

            Mocker.GetMock<IMoveEpisodeFiles>()
                  .Setup(c => c.CopyEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>()))
                  .Returns(new EpisodeFile { RelativePath = @"Season 01\30.rock.s01e01.new.mkv" });
        }

        private void GivenSingleEpisodeWithSingleEpisodeFile()
        {
            _localEpisode.Episodes = Builder<Episode>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.EpisodeFileId = 1)
                                                     .With(e => e.EpisodeFile = new EpisodeFile
                                                                                {
                                                                                    Id = 1,
                                                                                    RelativePath = @"Season 01\30.rock.s01e01.avi",
                                                                                })
                                                     .Build()
                                                     .ToList();

            StubParkedPaths();
        }

        private void GivenMultipleEpisodesWithSingleEpisodeFile()
        {
            _localEpisode.Episodes = Builder<Episode>.CreateListOfSize(2)
                                                     .All()
                                                     .With(e => e.EpisodeFileId = 1)
                                                     .With(e => e.EpisodeFile = new EpisodeFile
                                                                                {
                                                                                    Id = 1,
                                                                                    RelativePath = @"Season 01\30.rock.s01e01.avi",
                                                                                })
                                                     .Build()
                                                     .ToList();

            StubParkedPaths();
        }

        private void GivenMultipleEpisodesWithMultipleEpisodeFiles()
        {
            _localEpisode.Episodes = Builder<Episode>.CreateListOfSize(2)
                                                     .TheFirst(1)
                                                     .With(e => e.EpisodeFile = new EpisodeFile
                                                                                {
                                                                                    Id = 1,
                                                                                    RelativePath = @"Season 01\30.rock.s01e01.avi",
                                                                                })
                                                     .TheNext(1)
                                                     .With(e => e.EpisodeFile = new EpisodeFile
                                                                                {
                                                                                    Id = 2,
                                                                                    RelativePath = @"Season 01\30.rock.s01e02.avi",
                                                                                })
                                                     .Build()
                                                     .ToList();

            StubParkedPaths();
        }

        private string ExistingFilePath(EpisodeFile file)
        {
            return Path.Combine(_localEpisode.Series.Path, file.RelativePath);
        }

        // Each parked path is empty when the stale check runs, then holds the parked original for every
        // later look (finalize / restore). A test that wants a stale leftover overrides this.
        private void StubParkedPaths()
        {
            foreach (var file in _localEpisode.Episodes.Where(e => e.EpisodeFile != null).Select(e => e.EpisodeFile.Value).DistinctBy(f => f.Id))
            {
                Mocker.GetMock<IDiskProvider>()
                      .SetupSequence(c => c.FileExists(ExistingFilePath(file) + ParkedFileSuffix))
                      .Returns(false)
                      .Returns(true)
                      .Returns(true)
                      .Returns(true);
            }
        }

        private EpisodeFileMoveResult UpgradeAndFinalize()
        {
            var result = Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);
            Subject.FinalizeUpgrade(result);
            return result;
        }

        [Test]
        public void should_delete_single_episode_file_once()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_the_same_episode_file_only_once()
        {
            GivenMultipleEpisodesWithSingleEpisodeFile();

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Once());
        }

        [Test]
        public void should_delete_multiple_different_episode_files()
        {
            GivenMultipleEpisodesWithMultipleEpisodeFiles();

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Exactly(2));
        }

        [Test]
        public void should_delete_episode_file_from_database()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            UpgradeAndFinalize();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<EpisodeFile>(), DeleteMediaFileReason.Upgrade), Times.Once());
        }

        [Test]
        public void should_delete_existing_file_fromdb_if_file_doesnt_exist()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            UpgradeAndFinalize();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localEpisode.Episodes.Single().EpisodeFile, DeleteMediaFileReason.Upgrade), Times.Once());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_not_try_to_recyclebin_existing_file_if_file_doesnt_exist()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            Mocker.GetMock<IDiskProvider>()
                .Setup(c => c.FileExists(It.IsAny<string>()))
                .Returns(false);

            UpgradeAndFinalize();

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_return_old_episode_file_in_oldFiles()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            UpgradeAndFinalize().OldFiles.Count.Should().Be(1);
        }

        [Test]
        public void should_return_old_episode_files_in_oldFiles()
        {
            GivenMultipleEpisodesWithMultipleEpisodeFiles();

            UpgradeAndFinalize().OldFiles.Count.Should().Be(2);
        }

        [Test]
        public void should_expose_old_file_to_the_import_script_during_the_transfer()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var oldFilesSeenByMover = -1;

            // ScriptImportDecider runs inside the mover and reads localEpisode.OldFiles for
            // Sonarr_DeletedPaths; the entry must exist before FinalizeUpgrade.
            Mocker.GetMock<IMoveEpisodeFiles>()
                  .Setup(c => c.MoveEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>()))
                  .Callback<EpisodeFile, LocalEpisode>((f, l) => oldFilesSeenByMover = l.OldFiles.Count)
                  .Returns(new EpisodeFile { RelativePath = @"Season 01\30.rock.s01e01.new.mkv" });

            var result = Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);

            oldFilesSeenByMover.Should().Be(1);
            result.OldFiles.Single().EpisodeFile.Should().BeSameAs(_localEpisode.Episodes.Single().EpisodeFile.Value);
            result.OldFiles.Single().RecycleBinPath.Should().BeNull();
        }

        [Test]
        public void should_fill_in_recycle_bin_path_on_finalize_without_duplicating_old_files()
        {
            GivenMultipleEpisodesWithMultipleEpisodeFiles();

            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(c => c.DeleteFile(It.IsAny<string>(), It.IsAny<string>()))
                  .Returns<string, string>((path, subfolder) => Path.Combine(@"C:\Test\Recycle".AsOsAgnostic(), Path.GetFileName(path)));

            var result = UpgradeAndFinalize();

            result.OldFiles.Should().HaveCount(2);
            result.OldFiles.Should().OnlyContain(f => f.RecycleBinPath != null);
            _localEpisode.OldFiles.Should().BeSameAs(result.OldFiles);
        }

        [Test]
        public void should_recycle_a_stale_parked_file_instead_of_deleting_it_permanently()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var stalePath = ExistingFilePath(_localEpisode.Episodes.Single().EpisodeFile.Value) + ParkedFileSuffix;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(stalePath))
                  .Returns(true);

            Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(stalePath, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(stalePath), Times.Never());

            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void should_delete_a_stale_parked_file_permanently_when_recycling_fails()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var stalePath = ExistingFilePath(_localEpisode.Episodes.Single().EpisodeFile.Value) + ParkedFileSuffix;

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(stalePath))
                  .Returns(true);

            Mocker.GetMock<IRecycleBinProvider>()
                  .Setup(c => c.DeleteFile(stalePath, It.IsAny<string>()))
                  .Throws(new RecycleBinException("Simulated recycle bin failure"));

            Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(stalePath), Times.Once());

            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void should_throw_if_there_are_existing_episode_files_and_the_root_folder_is_missing()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FolderExists(Directory.GetParent(_localEpisode.Series.Path).FullName))
                  .Returns(false);

            Assert.Throws<RootFolderNotFoundException>(() => Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode));

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localEpisode.Episodes.Single().EpisodeFile, DeleteMediaFileReason.Upgrade), Times.Never());
        }

        [Test]
        public void should_import_if_existing_file_doesnt_exist_in_db()
        {
            _localEpisode.Episodes = Builder<Episode>.CreateListOfSize(1)
                                                     .All()
                                                     .With(e => e.EpisodeFileId = 1)
                                                     .With(e => e.EpisodeFile = new LazyLoaded<EpisodeFile>(null))
                                                     .Build()
                                                     .ToList();

            UpgradeAndFinalize();

            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(_localEpisode.Episodes.Single().EpisodeFile, It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_park_existing_file_before_moving_replacement()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var originalPath = Path.Combine(_localEpisode.Series.Path, _localEpisode.Episodes.First().EpisodeFile.Value.RelativePath);

            Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath, originalPath + ParkedFileSuffix, false), Times.Once());
        }

        [Test]
        public void should_not_delete_or_recycle_before_finalize()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<EpisodeFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_restore_parked_original_and_not_delete_when_replacement_transfer_fails()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var originalPath = Path.Combine(_localEpisode.Series.Path, _localEpisode.Episodes.First().EpisodeFile.Value.RelativePath);

            Mocker.GetMock<IMoveEpisodeFiles>()
                  .Setup(c => c.MoveEpisodeFile(It.IsAny<EpisodeFile>(), It.IsAny<LocalEpisode>()))
                  .Throws(new IOException("Simulated destination write failure"));

            Assert.Throws<IOException>(() => Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode));

            // Parked, then restored to its original location.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath, originalPath + ParkedFileSuffix, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());

            // The original must never be deleted or recycled when the replacement fails.
            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<EpisodeFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_remove_replacement_and_restore_original_on_rollback()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var originalPath = Path.Combine(_localEpisode.Series.Path, _localEpisode.Episodes.First().EpisodeFile.Value.RelativePath);
            var newPath = Path.Combine(_localEpisode.Series.Path, @"Season 01\30.rock.s01e01.new.mkv");

            var result = Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);
            Subject.RollbackUpgrade(result);

            // The source still exists (copy/hardlink import), so the freshly-placed replacement is
            // removed and the original restored.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(newPath), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());

            Mocker.GetMock<IRecycleBinProvider>().Verify(v => v.DeleteFile(It.IsAny<string>(), It.IsAny<string>()), Times.Never());
            Mocker.GetMock<IMediaFileService>().Verify(v => v.Delete(It.IsAny<EpisodeFile>(), It.IsAny<DeleteMediaFileReason>()), Times.Never());
        }

        [Test]
        public void should_return_moved_replacement_to_source_on_rollback()
        {
            GivenSingleEpisodeWithSingleEpisodeFile();

            var originalPath = Path.Combine(_localEpisode.Series.Path, _localEpisode.Episodes.First().EpisodeFile.Value.RelativePath);
            var newPath = Path.Combine(_localEpisode.Series.Path, @"Season 01\30.rock.s01e01.new.mkv");
            var sourcePath = _localEpisode.Path;

            // The source is gone (the file was moved into the library).
            Mocker.GetMock<IDiskProvider>()
                  .Setup(c => c.FileExists(sourcePath))
                  .Returns(false);

            // Present when parked, absent (no stray) when the original is restored.
            Mocker.GetMock<IDiskProvider>()
                  .SetupSequence(c => c.FileExists(originalPath))
                  .Returns(true)
                  .Returns(false);

            var result = Subject.UpgradeEpisodeFile(_episodeFile, _localEpisode);
            Subject.RollbackUpgrade(result);

            // The moved replacement goes back to the download location so it stays importable, and the
            // parked original is restored; nothing is permanently deleted.
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(newPath, sourcePath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.MoveFile(originalPath + ParkedFileSuffix, originalPath, false), Times.Once());
            Mocker.GetMock<IDiskProvider>().Verify(v => v.DeleteFile(newPath), Times.Never());
        }
    }
}
