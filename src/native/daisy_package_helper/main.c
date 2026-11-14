// A narrowly scoped PolicyKit entry point. All commits are prepared and checked
// again under libalpm's transaction lock; no dependency checks are disabled.
#include <alpm.h>
#include <openssl/evp.h>
#include <sys/stat.h>
#include <sys/wait.h>
#include <fcntl.h>
#include <unistd.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <limits.h>
#include <sys/utsname.h>
#include <sys/file.h>
#include <pwd.h>
#include <ftw.h>
#include <dirent.h>

static alpm_handle_t *handle;
static char staging[PATH_MAX];
static char package_hash[65];
static const char *test_root;
static void cleanup(void) { if(handle) alpm_release(handle); if(staging[0]) { unlink(staging); char sig[PATH_MAX+5];snprintf(sig,sizeof(sig),"%s.sig",staging);unlink(sig); } }
static void quoted(const char *text) {
    putchar('"'); for(const unsigned char *p=(const unsigned char *)(text?text:""); *p; ++p) {
        if(*p=='"'||*p=='\\') { putchar('\\'); putchar(*p); }
        else if(*p<' ') printf("\\u%04x",*p); else putchar(*p);
    } putchar('"');
}
static int fail(const char *message) { fprintf(stderr,"%s\n",message); return 1; }
static char *config(const char *repo,const char *key) {
    int fd[2]; if(pipe(fd)) return NULL; pid_t pid=fork();
    if(pid==0) { dup2(fd[1],STDOUT_FILENO); close(fd[0]); close(fd[1]);
        if(repo) execl("/usr/bin/pacman-conf","pacman-conf","--repo",repo,key,(char *)NULL);
        else execl("/usr/bin/pacman-conf","pacman-conf",key,(char *)NULL);
        _exit(127);
    }
    close(fd[1]); if(pid<0) { close(fd[0]); return NULL; }
    FILE *stream=fdopen(fd[0],"r"); char *result=calloc(1,1), *line=NULL; size_t n=0,length=0;
    if(!result||!stream) exit(1);
    while(getline(&line,&n,stream)>0) { size_t add=strlen(line); if(length+add>1024*1024) break;
        char *next=realloc(result,length+add+1); if(!next) exit(1); result=next; memcpy(result+length,line,add+1); length+=add; }
    free(line); fclose(stream); int status=0; waitpid(pid,&status,0);
    if(!WIFEXITED(status)||WEXITSTATUS(status)) { free(result); return NULL; } return result;
}
static int setup(void) {
    char database[PATH_MAX];snprintf(database,sizeof(database),"%s/var/lib/pacman",test_root?test_root:"");
    alpm_errno_t error; handle=alpm_initialize(test_root?test_root:"/",database,&error);
    if(!handle) return fail(alpm_strerror(error));
    struct utsname host; if(uname(&host)) return fail("Could not detect host architecture.");
    alpm_option_add_architecture(handle,host.machine);
    alpm_option_set_gpgdir(handle,"/etc/pacman.d/gnupg");
    alpm_option_add_cachedir(handle,"/var/cache/pacman/pkg");
    alpm_option_add_hookdir(handle,"/usr/share/libalpm/hooks");
    alpm_option_add_hookdir(handle,"/etc/pacman.d/hooks");
    char *repositories=config(NULL,"--repo-list"); if(!repositories) return fail("Could not read Pacman's repository configuration.");
    char *save=NULL;
    for(char *repo=strtok_r(repositories,"\n",&save); repo; repo=strtok_r(NULL,"\n",&save)) {
        alpm_db_t *db=alpm_register_syncdb(handle,repo,ALPM_SIG_PACKAGE|ALPM_SIG_DATABASE|ALPM_SIG_DATABASE_OPTIONAL);
        char *servers=config(repo,"Server");
        if(db&&servers) { char *next=NULL; for(char *server=strtok_r(servers,"\n",&next);server;server=strtok_r(NULL,"\n",&next)) alpm_db_add_server(db,server); }
        free(servers);
    }
    free(repositories); return 0;
}
static alpm_pkg_t *owner(const char *path) {
    if(*path=='/') ++path;
    for(alpm_list_t *i=alpm_db_get_pkgcache(alpm_get_localdb(handle));i;i=i->next) {
        alpm_pkg_t *pkg=i->data; if(alpm_filelist_contains(alpm_pkg_get_files(pkg),path)) return pkg;
    } return NULL;
}
static int contains(alpm_list_t *list,const char *name) { return alpm_list_find_str(list,name)!=NULL; }
static void add_name(alpm_list_t **list,const char *name) { if(name&&!contains(*list,name)) *list=alpm_list_add(*list,strdup(name)); }
static alpm_list_t *essential(void) {
    char path[PATH_MAX];snprintf(path,sizeof(path),"%s/usr/share/daisyos/essential-applications.conf",test_root?test_root:"");
    struct stat metadata; if(stat(path,&metadata)||metadata.st_uid!=(test_root?getuid():0)||(metadata.st_mode&0022)) return NULL;
    FILE *file=fopen(path,"r"); if(!file) return NULL;
    alpm_list_t *roots=NULL; char *line=NULL;size_t capacity=0;
    while(getline(&line,&capacity,file)>0) {
        line[strcspn(line,"\r\n")]=0; if(!*line||*line=='#') continue;
        alpm_pkg_t *pkg=strncmp(line,"file:",5)==0?owner(line+5):alpm_db_get_pkg(alpm_get_localdb(handle),line);
        if(pkg) add_name(&roots,alpm_pkg_get_name(pkg));
    }
    free(line);fclose(file);
    // Protect custom kernels and installed display-driver providers too.
    for(alpm_list_t *i=alpm_db_get_pkgcache(alpm_get_localdb(handle));i;i=i->next) {
        alpm_pkg_t *pkg=i->data; alpm_filelist_t *files=alpm_pkg_get_files(pkg);
        for(size_t index=0;index<files->count;++index) {
            const char *name=files->files[index].name;size_t length=strlen(name);
            if((strncmp(name,"usr/lib/modules/",16)==0&&length>8&&strcmp(name+length-8,"/vmlinuz")==0)||
               strncmp(name,"usr/lib/dri/",12)==0||strncmp(name,"usr/share/vulkan/icd.d/",23)==0) { add_name(&roots,alpm_pkg_get_name(pkg));break; }
        }
    }
    for(alpm_list_t *i=roots;i;i=i->next) {
        alpm_pkg_t *pkg=alpm_db_get_pkg(alpm_get_localdb(handle),i->data); if(!pkg) continue;
        for(alpm_list_t *d=alpm_pkg_get_depends(pkg);d;d=d->next) {
            char *dependency=alpm_dep_compute_string(d->data);
            alpm_pkg_t *provider=alpm_find_satisfier(alpm_db_get_pkgcache(alpm_get_localdb(handle)),dependency);
            if(provider) { add_name(&roots,alpm_pkg_get_name(provider)); }
            free(dependency);
        }
    }
    return roots;
}
static int compare(const void *a,const void *b) { return strcmp(*(char *const *)a,*(char *const *)b); }
static char **changes(size_t *count) {
    *count=alpm_list_count(alpm_trans_get_add(handle))+alpm_list_count(alpm_trans_get_remove(handle))+(package_hash[0]?1:0);
    char **list=calloc(*count+1,sizeof(char *)); if(!list) exit(1); size_t n=0;
    for(int side=0;side<2;++side) for(alpm_list_t *i=side?alpm_trans_get_remove(handle):alpm_trans_get_add(handle);i;i=i->next) {
        alpm_pkg_t *pkg=i->data; if(asprintf(&list[n++],"%s:%s=%s",side?"remove":"install",alpm_pkg_get_name(pkg),alpm_pkg_get_version(pkg))<0) exit(1);
    }
    if(package_hash[0]&&asprintf(&list[n++],"source:%s",package_hash)<0)exit(1);
    qsort(list,*count,sizeof(char *),compare);return list;
}
static void fingerprint(char **list,size_t count,char output[65]) {
    EVP_MD_CTX *ctx=EVP_MD_CTX_new();unsigned char digest[32];unsigned int size=0;
    EVP_DigestInit_ex(ctx,EVP_sha256(),NULL);
    for(size_t i=0;i<count;++i) { if(i) EVP_DigestUpdate(ctx,"\n",1); EVP_DigestUpdate(ctx,list[i],strlen(list[i])); }
    EVP_DigestFinal_ex(ctx,digest,&size);EVP_MD_CTX_free(ctx);
    for(unsigned int i=0;i<size;++i) { sprintf(output+i*2,"%02x",digest[i]); }
    output[64]=0;
}
static int stage(const char *source) {
    int input=open(source,O_RDONLY|O_NOFOLLOW|O_CLOEXEC);struct stat metadata;
    if(input<0||fstat(input,&metadata)||!S_ISREG(metadata.st_mode)||metadata.st_size>8LL*1024*1024*1024) { if(input>=0)close(input);return fail("Choose a regular package file, not a symbolic link."); }
    const char *uid=getenv("PKEXEC_UID");
    if(uid&&metadata.st_uid!=(uid_t)strtoul(uid,NULL,10)&&!(metadata.st_mode&S_IROTH)) { close(input);return fail("The package file is not readable by the requesting user."); }
    snprintf(staging,sizeof(staging),"/var/cache/pacman/daisy-package-XXXXXX");int output=mkstemp(staging);
    if(output<0) { close(input);staging[0]=0;return fail("Could not stage the package."); }
    char buffer[65536];ssize_t n;int failed=0;
    while((n=read(input,buffer,sizeof(buffer)))>0) { ssize_t done=0;while(done<n) { ssize_t wrote=write(output,buffer+done,(size_t)(n-done));if(wrote<=0){failed=1;break;}done+=wrote; }if(failed)break; }
    if(n<0||fsync(output)) { failed=1; }
    close(input);close(output);
    if(failed)return fail("Could not copy the package into protected staging.");
    char original_sig[PATH_MAX+5],staged_sig[PATH_MAX+5];
    snprintf(original_sig,sizeof(original_sig),"%s.sig",source);snprintf(staged_sig,sizeof(staged_sig),"%s.sig",staging);
    input=open(original_sig,O_RDONLY|O_NOFOLLOW|O_CLOEXEC);
    if(input>=0) {
        if(fstat(input,&metadata)||!S_ISREG(metadata.st_mode)||metadata.st_size>1024*1024) { close(input);return fail("Invalid package signature file."); }
        output=open(staged_sig,O_WRONLY|O_CREAT|O_EXCL|O_CLOEXEC,0600);if(output<0){close(input);return fail("Could not stage package signature.");}
        while((n=read(input,buffer,sizeof(buffer)))>0) { if(write(output,buffer,(size_t)n)!=n){failed=1;break;} }
        close(input);close(output);if(n<0||failed)return fail("Could not stage package signature.");
    }
    return 0;
}
static int hash_package(const char *path) {
    FILE *file=fopen(path,"rb");if(!file)return fail("Could not read package fingerprint.");
    EVP_MD_CTX *ctx=EVP_MD_CTX_new();if(!ctx)exit(1);EVP_DigestInit_ex(ctx,EVP_sha256(),NULL);
    char buffer[65536];size_t size;while((size=fread(buffer,1,sizeof(buffer),file)))EVP_DigestUpdate(ctx,buffer,size);
    int error=ferror(file);fclose(file);unsigned char digest[32];unsigned int length=0;
    EVP_DigestFinal_ex(ctx,digest,&length);EVP_MD_CTX_free(ctx);
    for(unsigned int i=0;i<length;i++) { sprintf(package_hash+i*2,"%02x",digest[i]); }
    package_hash[64]=0;return error?fail("Could not fingerprint package."):0;
}
static int configure_rootless(void) {
    const char *uid=getenv("PKEXEC_UID");if(!uid)return 0;
    struct passwd *user=getpwuid((uid_t)strtoul(uid,NULL,10));if(!user||user->pw_uid==0)return fail("Rootless compatibility support requires a regular user.");
    int lock=open("/var/lib/pacman/daisy-rootless.lock",O_CREAT|O_RDWR|O_CLOEXEC,0600);if(lock<0||flock(lock,LOCK_EX))return fail("Could not lock rootless user setup.");
    unsigned long first=100000;int has_uid=0,has_gid=0;
    for(int kind=0;kind<2;kind++) {
        FILE *file=fopen(kind?"/etc/subgid":"/etc/subuid","r");if(!file)continue;
        char *line=NULL;size_t cap=0;
        while(getline(&line,&cap,file)>0) { char *save=NULL;char *name=strtok_r(line,":",&save);char *start=strtok_r(NULL,":",&save);char *count=strtok_r(NULL,":",&save);if(!name||!start||!count)continue;
            if(!strcmp(name,user->pw_name)) { if(kind)has_gid=1;else has_uid=1; }
            unsigned long end=strtoul(start,NULL,10)+strtoul(count,NULL,10);if(end>first)first=end;
        }free(line);fclose(file);
    }
    if(first>UINT_MAX-65536) { close(lock);return fail("No subordinate UID range is available."); }
    for(int kind=0;kind<2;kind++)if(!(kind?has_gid:has_uid)) {
        FILE *file=fopen(kind?"/etc/subgid":"/etc/subuid","a");if(!file){close(lock);return fail("Could not configure subordinate user IDs.");}
        fprintf(file,"%s:%lu:65536\n",user->pw_name,first);if(fclose(file)){close(lock);return fail("Could not save subordinate user IDs.");}
    }close(lock);return 0;
}
static EVP_MD_CTX *portable_digest;
static dev_t portable_device;
static int portable_error;
static char *portable_launchers[256];
static size_t portable_launcher_count;
static int find_portable_launchers(const char *folder) {
    const char *directories[]={"/usr/share/applications","/usr/local/share/applications",NULL};
    for(int i=0;directories[i];i++) {
        char directory[PATH_MAX];snprintf(directory,sizeof(directory),"%s%s",test_root?test_root:"",directories[i]);
        char canonical[PATH_MAX];struct stat parent;
        if(!realpath(directory,canonical)||strcmp(canonical,directory)||stat(directory,&parent)||parent.st_uid!=(test_root?getuid():0)||(parent.st_mode&0022))continue;
        DIR *stream=opendir(directory);if(!stream)continue;
        struct dirent *entry;
        while((entry=readdir(stream))) {
            size_t length=strlen(entry->d_name);if(length<8||strcmp(entry->d_name+length-8,".desktop"))continue;
            char path[PATH_MAX];if(snprintf(path,sizeof(path),"%s/%s",directory,entry->d_name)>=(int)sizeof(path))continue;
            struct stat info;if(lstat(path,&info)||!S_ISREG(info.st_mode)||info.st_uid!=(test_root?getuid():0)||(info.st_mode&0022)||info.st_size>1024*1024)continue;
            const char *relative=test_root?path+strlen(test_root):path;if(owner(relative))continue;
            FILE *file=fopen(path,"r");if(!file)continue;
            char *line=NULL;size_t capacity=0;int matches=0;
            while(getline(&line,&capacity,file)>0) {
                if(strncmp(line,"Exec=",5))continue;
                char *command=line+5;while(*command==' '||*command=='\t')command++;if(*command=='"')command++;
                if(!strncmp(command,folder,strlen(folder))&&command[strlen(folder)]=='/')matches=1;
            }
            free(line);
            if(matches) {
                if(portable_launcher_count==256){fclose(file);closedir(stream);return fail("Too many application launchers to review safely.");}
                portable_launchers[portable_launcher_count++]=strdup(path);
                EVP_DigestUpdate(portable_digest,path,strlen(path));
                rewind(file);char bytes[4096];size_t count;while((count=fread(bytes,1,sizeof(bytes),file)))EVP_DigestUpdate(portable_digest,bytes,count);
                if(ferror(file)){fclose(file);closedir(stream);return fail("Could not inspect an application launcher.");}
            }
            fclose(file);
        }
        closedir(stream);
    }
    return 0;
}
static int inspect_portable(const char *path,const struct stat *info,int type,struct FTW *walk) {
    (void)walk;
    if(type==FTW_DNR||type==FTW_NS||info->st_dev!=portable_device||info->st_uid!=(test_root?getuid():0)||(!S_ISLNK(info->st_mode)&&(info->st_mode&0022))) { portable_error=1;return 1; }
    const char *relative=path;if(test_root&&strncmp(path,test_root,strlen(test_root))==0)relative=path+strlen(test_root);
    if(owner(relative)) { portable_error=1;return 1; }
    const char *base=strrchr(path,'/');base=base?base+1:path;
    const char *critical[]={"systemd","kwin_wayland","NetworkManager","pkexec","dbus-daemon","daisyos-shell","daisyos-settings","daisyos-appinstaller",NULL};
    for(int i=0;critical[i];i++)if(!strcmp(base,critical[i])){portable_error=1;return 1;}
    EVP_DigestUpdate(portable_digest,path,strlen(path));
    EVP_DigestUpdate(portable_digest,&info->st_size,sizeof(info->st_size));
    EVP_DigestUpdate(portable_digest,&info->st_mtim,sizeof(info->st_mtim));
    return 0;
}
static int delete_portable(const char *path,const struct stat *info,int type,struct FTW *walk) {
    (void)info;(void)walk;
    return type==FTW_DP?rmdir(path):unlink(path);
}
static int portable(const char *target,int commit,const char *approved) {
    char resolved[PATH_MAX];if(!realpath(target,resolved)||strcmp(target,resolved))return fail("Choose a real application folder without symbolic links.");
    const char *relative=resolved;if(test_root&&strncmp(resolved,test_root,strlen(test_root))==0)relative=resolved+strlen(test_root);
    const char *prefixes[]={"/opt/","/usr/local/lib/","/usr/local/share/",NULL};int allowed=0;
    for(int i=0;prefixes[i];i++)if(!strncmp(relative,prefixes[i],strlen(prefixes[i]))&&strlen(relative)>strlen(prefixes[i]))allowed=1;
    if(!allowed||strstr(relative,"/DaisyOS")||strstr(relative,"/daisyos"))return fail("Only a dedicated, unowned application folder in /opt or /usr/local/lib or /usr/local/share may be removed. Use the package manager for owned files.");
    struct stat info;if(lstat(resolved,&info)||!S_ISDIR(info.st_mode))return fail("The reviewed application folder no longer exists.");
    char parent[PATH_MAX];snprintf(parent,sizeof(parent),"%s",resolved);
    for(char *slash=strrchr(parent,'/');slash&&slash!=parent;slash=strrchr(parent,'/')) {
        *slash=0;struct stat ancestor;
        if(lstat(parent,&ancestor)||!S_ISDIR(ancestor.st_mode)||ancestor.st_uid!=(test_root?getuid():0)||(ancestor.st_mode&0022))return fail("The application's parent folders must be administrator-owned and protected from changes.");
        if(test_root&&!strcmp(parent,test_root))break;
    }
    portable_device=info.st_dev;portable_error=0;portable_digest=EVP_MD_CTX_new();if(!portable_digest)exit(1);EVP_DigestInit_ex(portable_digest,EVP_sha256(),NULL);
    if(nftw(resolved,inspect_portable,32,FTW_PHYS|FTW_MOUNT)||portable_error) { EVP_MD_CTX_free(portable_digest);return fail("This folder contains package-owned, shared, writable or system-critical files. It cannot be removed as an unmanaged app."); }
    if(find_portable_launchers(relative)){EVP_MD_CTX_free(portable_digest);return 1;}
    unsigned char digest[32];unsigned int length=0;char hash[65];EVP_DigestFinal_ex(portable_digest,digest,&length);EVP_MD_CTX_free(portable_digest);
    for(unsigned int i=0;i<length;i++){sprintf(hash+i*2,"%02x",digest[i]);}hash[64]=0;
    char *entry=NULL;if(asprintf(&entry,"portable:%s=%s",resolved,hash)<0)exit(1);char fingerprint_hash[65];char *entries[]={entry};fingerprint(entries,1,fingerprint_hash);
    if(commit) {
        if(strcmp(approved,fingerprint_hash)){free(entry);return fail("The reviewed application folder changed. Review it again before removing it.");}
        if(nftw(resolved,delete_portable,32,FTW_PHYS|FTW_DEPTH|FTW_MOUNT)){free(entry);return fail("The application folder could not be completely removed. Review the remaining files before retrying.");}
        for(size_t i=0;i<portable_launcher_count;i++)if(unlink(portable_launchers[i]))return fail("The app was removed, but a system shortcut needs cleanup.");
        puts("{\"completed\":true}");
    } else {
        printf("{\"packages\":[");quoted(entry);printf("],\"desktopFiles\":[");
        for(size_t i=0;i<portable_launcher_count;i++){if(i)putchar(',');quoted(test_root?portable_launchers[i]+strlen(test_root):portable_launchers[i]);}
        printf("],\"name\":");quoted(strrchr(resolved,'/')+1);printf("}\n");
    }
    free(entry);return 0;
}
static int inventory(void) {
    if(setup())return 1;
    alpm_list_t *protected=essential();if(!protected)return fail("The essential-component manifest is missing or invalid.");
    int first=1;putchar('[');
    for(alpm_list_t *i=alpm_db_get_pkgcache(alpm_get_localdb(handle));i;i=i->next) {
        alpm_pkg_t *pkg=i->data;alpm_filelist_t *files=alpm_pkg_get_files(pkg);int any=0;
        for(size_t j=0;j<files->count;j++) { const char *name=files->files[j].name;size_t length=strlen(name);
            if(strstr(name,"share/applications/")&&length>8&&!strcmp(name+length-8,".desktop"))any=1;
        }
        if(!any)continue;
        if(!first) { putchar(','); } first=0;
        printf("{\"identity\":{\"backend\":0,\"packageId\":");quoted(alpm_pkg_get_name(pkg));printf(",\"scope\":\"system\"},\"name\":");quoted(alpm_pkg_get_name(pkg));
        printf(",\"version\":");quoted(alpm_pkg_get_version(pkg));
        if(contains(protected,alpm_pkg_get_name(pkg)))printf(",\"isProtected\":true,\"protectionReason\":\"Required for system operation or recovery.\"");
        for(int side=0;side<2;side++) {
            printf(side?",\"ownedLaunchers\":[":",\"desktopIds\":[");int initial=1;
            for(size_t j=0;j<files->count;j++) { const char *name=files->files[j].name;size_t length=strlen(name);
                if(strstr(name,"share/applications/")&&length>8&&!strcmp(name+length-8,".desktop")) {
                    if(!initial) { putchar(','); } initial=0;
                    char full[PATH_MAX];snprintf(full,sizeof(full),"/%s",name);quoted(side?full:strrchr(name,'/')+1);
                }
            }putchar(']');
        }putchar('}');
    }puts("]");return 0;
}
int main(int argc,char **argv) {
    atexit(cleanup);
    // Only read-only, unprivileged tests may override the database root. Root
    // commits always use the real system database and root-owned policy.
    if(geteuid()!=0) { const char *root=getenv("DAISYOS_PACKAGE_TEST_ROOT");if(root&&root[0]=='/')test_root=root; }
    if(argc==2&&!strcmp(argv[1],"list"))return inventory();
    if(argc<3||argc>4) return fail("Usage: daisy-package-helper plan-install|plan-remove|install|remove TARGET [PLAN_SHA256]");
    int portable_remove=!strcmp(argv[1],"portable-remove")||!strcmp(argv[1],"plan-portable-remove");
    int flatpak_tools=strcmp(argv[1],"flatpak-tools")==0||strcmp(argv[1],"plan-flatpak")==0;
    int tools=flatpak_tools||strcmp(argv[1],"tools")==0||strcmp(argv[1],"plan-tools")==0;
    int repair=strcmp(argv[1],"repair")==0||strcmp(argv[1],"plan-repair")==0;
    int install=repair||tools||strcmp(argv[1],"install")==0||strcmp(argv[1],"plan-install")==0;
    int commit=strcmp(argv[1],"install")==0||strcmp(argv[1],"remove")==0||strcmp(argv[1],"tools")==0||strcmp(argv[1],"flatpak-tools")==0||strcmp(argv[1],"repair")==0||!strcmp(argv[1],"portable-remove");
    if(!portable_remove&&!install&&strcmp(argv[1],"remove")&&strcmp(argv[1],"plan-remove"))return fail("Unsupported operation.");
    if(commit&&(geteuid()!=0||argc!=4||strlen(argv[3])!=64))return fail("An authenticated, reviewed transaction is required.");
    if(setup())return 1;
    alpm_list_t *protected=essential();if(!protected)return fail("The root-owned essential-component manifest is missing or invalid. Repair DaisyOS system components first.");
    alpm_transflag_t flags=commit?0:ALPM_TRANS_FLAG_NOLOCK;
    if(!install)flags|=ALPM_TRANS_FLAG_CASCADE|ALPM_TRANS_FLAG_RECURSE;
    if(alpm_trans_init(handle,flags))return fail(alpm_strerror(alpm_errno(handle)));
    if(portable_remove)return portable(argv[2],commit,argc==4?argv[3]:"");
    alpm_pkg_t *selected=NULL;
    if(tools) {
        const char *compatibility_names[]={"podman","distrobox","fuse-overlayfs","shadow",NULL};
        const char *flatpak_names[]={"flatpak",NULL};
        const char **names=flatpak_tools?flatpak_names:compatibility_names;
        for(int i=0;names[i];i++) {
            if(alpm_db_get_pkg(alpm_get_localdb(handle),names[i]))continue;
            alpm_pkg_t *pkg=alpm_find_dbs_satisfier(handle,alpm_get_syncdbs(handle),names[i]);
            if(!pkg||alpm_add_pkg(handle,pkg))return fail("Compatibility prerequisites are unavailable. Update the system repositories first.");
            if(!selected)selected=pkg;
        }
    } else if(repair) {
        selected=alpm_find_dbs_satisfier(handle,alpm_get_syncdbs(handle),argv[2]);
        if(!selected||alpm_add_pkg(handle,selected))return fail("This package is not available in configured repositories. Use its original package to repair it.");
    } else if(install) {
        const char *path=argv[2];if(commit) { if(stage(path))return 1;path=staging; }
        if(hash_package(path))return 1;
        if(alpm_pkg_load(handle,path,1,ALPM_SIG_PACKAGE|ALPM_SIG_PACKAGE_OPTIONAL,&selected)||alpm_add_pkg(handle,selected))return fail(alpm_strerror(alpm_errno(handle)));
    } else {
        selected=alpm_db_get_pkg(alpm_get_localdb(handle),argv[2]);if(!selected)return fail("The package is no longer installed.");
        if(alpm_remove_pkg(handle,selected))return fail(alpm_strerror(alpm_errno(handle)));
    }
    alpm_list_t *errors=NULL;
    if(alpm_trans_prepare(handle,&errors))return fail(alpm_strerror(alpm_errno(handle)));
    int blocked=0;
    for(alpm_list_t *i=alpm_trans_get_remove(handle);i;i=i->next)
        if(contains(protected,alpm_pkg_get_name(i->data)))blocked=1;
    size_t count=0;char **list=changes(&count);char hash[65];fingerprint(list,count,hash);
    if(commit) {
        if(blocked)return fail("Removal would break essential DaisyOS components. No packages were changed.");
        if(strcmp(hash,argv[3]))return fail("The package transaction changed. Review it again before continuing.");
        if((alpm_trans_get_add(handle)||alpm_trans_get_remove(handle))&&alpm_trans_commit(handle,&errors))return fail(alpm_strerror(alpm_errno(handle)));
        if(tools&&!flatpak_tools&&configure_rootless())return 1;
        alpm_trans_release(handle); puts("{\"completed\":true}"); return 0;
    }
    printf("{\"name\":");quoted(selected?alpm_pkg_get_name(selected):"Compatibility support");printf(",\"version\":");quoted(selected?alpm_pkg_get_version(selected):"");
    printf(",\"architecture\":");quoted(selected?alpm_pkg_get_arch(selected):"");
    printf(",\"protected\":%s,\"error\":",blocked?"true":"false");quoted(blocked?"This transaction would remove components required for a working DaisyOS session.":"");
    printf(",\"packages\":[");for(size_t i=0;i<count;++i){if(i)putchar(',');quoted(list[i]);}printf("],\"desktopFiles\":[");
    int first=1;
    for(alpm_list_t *i=install?alpm_trans_get_add(handle):alpm_trans_get_remove(handle);i;i=i->next) {
        alpm_filelist_t *files=alpm_pkg_get_files(i->data);
        for(size_t j=0;j<files->count;++j) { const char *name=files->files[j].name;size_t length=strlen(name);
            if(strstr(name,"share/applications/")&&length>8&&!strcmp(name+length-8,".desktop")) { if(!first) { putchar(','); } first=0;char full[PATH_MAX];snprintf(full,sizeof(full),"/%s",name);quoted(full); }
        }
    }
    printf("]}\n");alpm_trans_release(handle);for(size_t i=0;i<count;++i)free(list[i]);free(list);alpm_list_free_inner(protected,free);alpm_list_free(protected);return 0;
}
