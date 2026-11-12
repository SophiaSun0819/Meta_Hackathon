package com.thinkingspace.sessiontransfer;

import android.app.Activity;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.net.Uri;
import android.os.Bundle;
import android.provider.OpenableColumns;
import android.util.Base64;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

public final class ThinkingSpaceDocumentPickerActivity extends Activity {
    private static final int PICK_DOCUMENT=9042;
    private String operation="";
    private String callbackObject="";
    private String path="";
    private int maximumBytes=4*1024*1024;

    public static boolean isPickerAvailable(){
        Activity activity=UnityPlayer.currentActivity;
        if(activity==null)return false;
        Intent open=new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json");
        Intent create=new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json");
        PackageManager manager=activity.getPackageManager();
        return open.resolveActivity(manager)!=null&&create.resolveActivity(manager)!=null;
    }

    public static void begin(String operation,String callbackObject,String path,String suggestedName,int maximumBytes){
        Activity activity=UnityPlayer.currentActivity;
        if(activity==null)throw new IllegalStateException("Unity activity is unavailable.");
        Intent proxy=new Intent(activity,ThinkingSpaceDocumentPickerActivity.class);
        proxy.putExtra("operation",operation);proxy.putExtra("callbackObject",callbackObject);
        proxy.putExtra("path",path);proxy.putExtra("suggestedName",suggestedName);
        proxy.putExtra("maximumBytes",maximumBytes);activity.startActivity(proxy);
    }

    @Override protected void onCreate(Bundle state){
        super.onCreate(state);
        Intent source=getIntent();operation=source.getStringExtra("operation");
        callbackObject=source.getStringExtra("callbackObject");path=source.getStringExtra("path");
        maximumBytes=Math.max(1,source.getIntExtra("maximumBytes",4*1024*1024));
        Intent picker;
        if("export".equals(operation)){
            picker=new Intent(Intent.ACTION_CREATE_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json");
            picker.putExtra(Intent.EXTRA_TITLE,source.getStringExtra("suggestedName"));
        }else if("import".equals(operation)){
            picker=new Intent(Intent.ACTION_OPEN_DOCUMENT).addCategory(Intent.CATEGORY_OPENABLE).setType("application/json");
        }else{finishWith("error","Unsupported document operation.","");return;}
        if(picker.resolveActivity(getPackageManager())==null){finishWith("error","Quest document picker is unavailable.","");return;}
        try{startActivityForResult(picker,PICK_DOCUMENT);}
        catch(Exception exception){finishWith("error","Quest document picker could not open.","");}
    }

    @Override protected void onActivityResult(int requestCode,int resultCode,Intent data){
        super.onActivityResult(requestCode,resultCode,data);
        if(requestCode!=PICK_DOCUMENT)return;
        if(resultCode!=RESULT_OK||data==null||data.getData()==null){finishWith("cancelled","Document selection cancelled.","");return;}
        Uri uri=data.getData();
        try{
            if("export".equals(operation))writeExport(uri);
            else readImport(uri);
        }catch(Exception exception){finishWith("error","Document transfer failed: "+safeReason(exception),"");}
    }

    private void writeExport(Uri uri)throws Exception{
        File source=new File(path);
        if(!source.isFile()||source.length()<=0||source.length()>maximumBytes)throw new IllegalArgumentException("prepared export is invalid");
        try(InputStream input=new FileInputStream(source);OutputStream output=getContentResolver().openOutputStream(uri,"wt")){
            if(output==null)throw new IllegalStateException("destination stream unavailable");
            copyBounded(input,output,maximumBytes);output.flush();
        }
        finishWith("success","Export saved as "+displayName(uri)+".","");
    }

    private void readImport(Uri uri)throws Exception{
        File directory=new File(path);
        if(!directory.exists()&&!directory.mkdirs())throw new IllegalStateException("import staging directory unavailable");
        File destination=File.createTempFile("selected-session-",".json",directory);
        boolean complete=false;
        try(InputStream input=getContentResolver().openInputStream(uri);OutputStream output=new FileOutputStream(destination)){
            if(input==null)throw new IllegalStateException("source stream unavailable");
            copyBounded(input,output,maximumBytes);output.flush();complete=true;
        }finally{if(!complete)destination.delete();}
        finishWith("success","Selected "+displayName(uri)+". Select Import Session again to confirm.",destination.getAbsolutePath());
    }

    private static void copyBounded(InputStream input,OutputStream output,int maximum)throws Exception{
        byte[] buffer=new byte[16384];int total=0,read;
        while((read=input.read(buffer))!=-1){total+=read;if(total>maximum)throw new IllegalArgumentException("document exceeds the 4 MB limit");output.write(buffer,0,read);}
        if(total<=0)throw new IllegalArgumentException("document is empty");
    }

    private String displayName(Uri uri){
        String value="Thinking Space session";
        try(Cursor cursor=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){
            if(cursor!=null&&cursor.moveToFirst()){String found=cursor.getString(0);if(found!=null&&!found.trim().isEmpty())value=found;}
        }catch(Exception ignored){}
        return value.replace('\n',' ').replace('\r',' ');
    }

    private static String safeReason(Exception exception){
        if(exception instanceof SecurityException)return "permission denied";
        if(exception instanceof IllegalArgumentException)return exception.getMessage();
        return "I/O error";
    }

    private void finishWith(String status,String message,String resultPath){
        String payload=operation+"|"+status+"|"+encode(message)+"|"+encode(resultPath);
        if(callbackObject!=null&&!callbackObject.isEmpty())UnityPlayer.UnitySendMessage(callbackObject,"OnSafDocumentResult",payload);
        finish();
    }

    private static String encode(String value){
        return Base64.encodeToString((value==null?"":value).getBytes(StandardCharsets.UTF_8),Base64.NO_WRAP);
    }
}
